using System;
using System.Collections.Generic;
using Chatting;
using ModLoaderInterfaces;
using NetworkUI;
using NetworkUI.Items;
using Newtonsoft.Json.Linq;
using Recipes;
using Shared;
using UnityEngine;

namespace BetterTrader
{
    [ModLoader.ModManager]
    public sealed class BetterTraderEntry : IAfterItemTypesDefined, IOnPlayerClicked, IOnPlayerPushedNetworkUIButton
    {
        private const string ButtonIdSelect = "bettertrader.select";
        private const string ButtonIdTrade = "bettertrader.trade";
        private const string StorageSelectedRecipe = "bettertrader.selectedRecipeKey";
        private const string StorageQuantity = "bettertrader.quantity";

        private const int MenuWidth = 980;
        private const int MenuHeight = 760;
        private const int TableWidth = 940;
        private const int TableHeight = 180;
        private const int RowHeight = 30;
        private const int MaxTradeBatchCount = 100000;
        private const long TradeCooldownTicks = 250L * TimeSpan.TicksPerMillisecond;

        private static readonly object s_tradeLock = new object();
        private static readonly HashSet<ushort> s_merchantHubTypes = new HashSet<ushort>();
        private static readonly Dictionary<int, long> s_nextTradeTicksByPlayer = new Dictionary<int, long>();
        private static ushort s_pointsType;

        public void AfterItemTypesDefined()
        {
            s_pointsType = ItemTypes.GetType("points").ItemIndex;
            s_merchantHubTypes.Clear();

            RegisterMerchantHubType("merchanthub");
            RegisterMerchantHubType("merchanthubx-");
            RegisterMerchantHubType("merchanthubx+");
            RegisterMerchantHubType("merchanthubz-");
            RegisterMerchantHubType("merchanthubz+");
            RegisterMerchantHubType("merchanthub2");
            RegisterMerchantHubType("merchanthub2x-");
            RegisterMerchantHubType("merchanthub2x+");
            RegisterMerchantHubType("merchanthub2z-");
            RegisterMerchantHubType("merchanthub2z+");
            RegisterMerchantHubType("bettertraderhub");
            RegisterMerchantHubType("bettertraderhubx-");
            RegisterMerchantHubType("bettertraderhubx+");
            RegisterMerchantHubType("bettertraderhubz-");
            RegisterMerchantHubType("bettertraderhubz+");
        }

        public void OnPlayerClicked(Players.Player player, PlayerClickedData click)
        {
            if (player == null || !IsReservedPopupClick(click))
            {
                return;
            }

            if (click.HitType != PlayerClickedData.EHitType.Block)
            {
                return;
            }

            PlayerClickedData.VoxelHit voxelHit = click.GetVoxelHit();
            if (!s_merchantHubTypes.Contains(voxelHit.TypeHit))
            {
                return;
            }

            TryOpenTraderPopup(player);
        }

        private static void RegisterMerchantHubType(string typeName)
        {
            s_merchantHubTypes.Add(ItemTypes.GetType(typeName).ItemIndex);
        }

        public void OnPlayerPushedNetworkUIButton(ButtonPressCallbackData data)
        {
            if (data.Player == null)
            {
                return;
            }

            if (!TryGetColonyGroup(data.Storage, out ColonyGroup group) || !group.HasOwner(data.Player))
            {
                return;
            }

            string quantityText = GetQuantityText(data.Storage);

            if (data.ButtonIdentifier == ButtonIdSelect)
            {
                uint selectedRecipeKey = data.ButtonPayload?.Value<uint>("recipeKey") ?? 0u;
                SendTraderPopup(data.Player, group, selectedRecipeKey, quantityText);
                return;
            }

            if (data.ButtonIdentifier != ButtonIdTrade)
            {
                return;
            }

            uint recipeKeyValue = data.ButtonPayload?.Value<uint>("recipeKey")
                ?? (uint?)data.Storage?.Value<int?>(StorageSelectedRecipe)
                ?? 0u;

            if (recipeKeyValue == 0u)
            {
                SendTraderPopup(data.Player, group, null, quantityText);
                return;
            }

            RecipeKey recipeKey = new RecipeKey(recipeKeyValue);
            if (!ServerManager.RecipeStorage.Recipes.TryGetValue(recipeKey, out Recipe recipe))
            {
                SendTraderPopup(data.Player, group, null, quantityText);
                return;
            }

            bool isMax = data.ButtonPayload?.Value<bool?>("isMax") ?? false;

            int requestedCount;
            if (isMax)
            {
                requestedCount = int.MaxValue;
            }
            else if (!TryParsePositiveInt(quantityText, out requestedCount))
            {
                Chat.Send(data.Player, T(data.Player, "Better Trader: enter a valid quantity greater than 0.", "Better Trader: bitte eine gueltige Menge groesser als 0 eingeben."));
                SendTraderPopup(data.Player, group, recipeKeyValue, quantityText);
                return;
            }

            if (!TryReserveTradeSlot(data.Player))
            {
                return;
            }

            int executedCount;
            lock (s_tradeLock)
            {
                int maxCount = GetMaxTradeCount(group, recipe);
                requestedCount = Math.Min(requestedCount, Math.Min(maxCount, MaxTradeBatchCount));
                executedCount = ProcessPointRecipeBatch(group, recipe, requestedCount);
            }

            if (executedCount <= 0)
            {
                Chat.Send(data.Player, T(data.Player, "Better Trader: that trade is not possible right now.", "Better Trader: dieser Handel ist gerade nicht moeglich."));
            }

            SendTraderPopup(data.Player, group, recipeKeyValue, quantityText);
        }

        internal static bool TryOpenTraderPopup(Players.Player player)
        {
            if (player == null)
            {
                return false;
            }

            ColonyGroup group = player.ActiveColonyGroup;
            if (group == null)
            {
                Chat.Send(player, T(player, "Better Trader: no active colony selected.", "Better Trader: keine aktive Kolonie ausgewaehlt."));
                return false;
            }

            if (!group.HasOwner(player))
            {
                Chat.Send(player, T(player, "Better Trader: you need to own the active colony.", "Better Trader: du musst Besitzer der aktiven Kolonie sein."));
                return false;
            }

            SendTraderPopup(player, group, null, null);
            return true;
        }

        private static void SendTraderPopup(Players.Player player, ColonyGroup group, uint? selectedRecipeKey, string quantityText)
        {
            NetworkMenu menu = new NetworkMenu
            {
                Width = MenuWidth,
                Height = MenuHeight,
                ForceClosePopups = true
            };

            List<TradeRecipeView> buyTrades = new List<TradeRecipeView>();
            List<TradeRecipeView> sellTrades = new List<TradeRecipeView>();
            CollectTradeRecipes(group, buyTrades, sellTrades);

            string normalizedQuantityText = NormalizeQuantityText(quantityText);
            bool hasSelectedTrade = TryResolveSelectedTrade(buyTrades, sellTrades, selectedRecipeKey, out TradeRecipeView selectedTrade);
            uint selectedRecipeKeyValue = hasSelectedTrade ? selectedTrade.Recipe.Key.Index : 0u;

            menu.LocalStorage["header"] = "Better Trader";
            menu.LocalStorage["colonyGroupID"] = group.ColonyGroupID.Value;
            menu.LocalStorage[StorageQuantity] = normalizedQuantityText;
            if (selectedRecipeKeyValue != 0u)
            {
                menu.LocalStorage[StorageSelectedRecipe] = (int)selectedRecipeKeyValue;
            }

            AddTradeSection(
                menu,
                player,
                group,
                T(player, "Buy", "Kaufen"),
                T(player, "Cost", "Preis"),
                buyTrades,
                selectedRecipeKeyValue,
                T(player, "No buy offers unlocked yet.", "Noch keine Kaufangebote freigeschaltet."));

            menu.Items.Add(new EmptySpace(6));

            AddTradeSection(
                menu,
                player,
                group,
                T(player, "Sell", "Verkaufen"),
                T(player, "Payout", "Ertrag"),
                sellTrades,
                selectedRecipeKeyValue,
                T(player, "No sell offers unlocked yet.", "Noch keine Verkaufsangebote freigeschaltet."));

            menu.Items.Add(new EmptySpace(6));
            AddSelectedTradeSection(menu, player, group, hasSelectedTrade, selectedTrade, normalizedQuantityText);

            NetworkMenuManager.SendServerPopup(player, menu);
        }

        private static void AddTradeSection(
            NetworkMenu menu,
            Players.Player player,
            ColonyGroup group,
            string title,
            string rateHeader,
            List<TradeRecipeView> trades,
            uint selectedRecipeKey,
            string emptyText)
        {
            menu.Items.Add(CreateParagraphLabel(title, 19));

            Table table = new Table(TableWidth, TableHeight)
            {
                AutoExpandHeight = false,
                ExternalMarginHorizontal = 3f
            };

            table.Header = new BackgroundColor(
                new HorizontalRow(new List<(IItem, int)>
                {
                    (CreateRowLabel(string.Empty, 16), 44),
                    (CreateRowLabel(T(player, "Item", "Item"), 16), 360),
                    (CreateRowLabel(rateHeader, 16), 150),
                    (CreateCenteredRowLabel(T(player, "Max", "Max"), 16), 110),
                    (CreateCenteredRowLabel(T(player, "Action", "Aktion"), 16), 140)
                }, RowHeight),
                -1,
                -1,
                0f,
                0f,
                4f,
                4f,
                Table.HEADER_COLOR);

            table.Rows = new List<IItem>();

            if (trades.Count == 0)
            {
                table.Rows.Add(new BackgroundColor(
                    new HorizontalRow(new List<(IItem, int)>
                    {
                        (CreateRowLabel(emptyText, 16), 804)
                    }, RowHeight),
                    -1,
                    20,
                    0f,
                    0f,
                    4f,
                    4f,
                    Table.ITEM_BG_COLOR));

                menu.Items.Add(table);
                return;
            }

            for (int i = 0; i < trades.Count; i++)
            {
                TradeRecipeView trade = trades[i];
                int maxCount = GetVisibleMaxTradeCount(group, trade.Recipe);
                bool isSelected = trade.Recipe.Key.Index == selectedRecipeKey;

                string itemName = Localization.GetType(player.LastKnownLocale, ItemTypes.GetType(trade.ItemType));
                string amountAndName = trade.ItemAmount > 1
                    ? string.Format("{0}x {1}", trade.ItemAmount, itemName)
                    : itemName;
                string pointsLabel = Localization.GetType(player.LastKnownLocale, ItemTypes.GetType(s_pointsType));

                IItem selectItem = isSelected
                    ? CreateCenteredRowLabel(T(player, "Selected", "Ausgewaehlt"), 14)
                    : CreateActionButton(
                        ButtonIdSelect,
                        T(player, "Select", "Waehlen"),
                        new JObject { { "recipeKey", trade.Recipe.Key.Index } },
                        120,
                        true);

                Color32 rowColor = isSelected ? new Color32(123, 98, 70, byte.MaxValue) : Table.ITEM_BG_COLOR;

                table.Rows.Add(new BackgroundColor(
                    new HorizontalRow(new List<(IItem, int)>
                    {
                        (new ItemIcon(trade.ItemType, 36), 44),
                        (CreateRowLabel(amountAndName, 16), 360),
                        (CreateRowLabel(string.Format("{0} {1}", trade.PointsAmount, pointsLabel), 16), 150),
                        (CreateCenteredRowLabel(maxCount.ToString(), 16), 110),
                        (selectItem, 140)
                    }, RowHeight),
                    -1,
                    20,
                    0f,
                    0f,
                    4f,
                    4f,
                    rowColor));
            }

            menu.Items.Add(table);
        }

        private static void AddSelectedTradeSection(
            NetworkMenu menu,
            Players.Player player,
            ColonyGroup group,
            bool hasSelectedTrade,
            TradeRecipeView selectedTrade,
            string quantityText)
        {
            menu.Items.Add(CreateParagraphLabel(T(player, "Selected Trade", "Ausgewaehlter Handel"), 19));

            if (!hasSelectedTrade)
            {
                menu.Items.Add(CreateParagraphLabel(
                    T(player, "No merchant recipes are currently available.", "Aktuell sind keine Haendlerrezepte verfuegbar."),
                    16));
                return;
            }

            int maxCount = GetMaxTradeCount(group, selectedTrade.Recipe);
            int visibleMaxCount = Math.Min(maxCount, MaxTradeBatchCount);
            string itemName = Localization.GetType(player.LastKnownLocale, ItemTypes.GetType(selectedTrade.ItemType));
            string amountAndName = selectedTrade.ItemAmount > 1
                ? string.Format("{0}x {1}", selectedTrade.ItemAmount, itemName)
                : itemName;

            string rateText = selectedTrade.IsBuy
                ? string.Format(
                    T(player, "Buying {0} costs {1} points per trade.", "Der Kauf von {0} kostet {1} Punkte pro Handel."),
                    amountAndName,
                    selectedTrade.PointsAmount)
                : string.Format(
                    T(player, "Selling {0} gives {1} points per trade.", "Der Verkauf von {0} bringt {1} Punkte pro Handel."),
                    amountAndName,
                    selectedTrade.PointsAmount);

            menu.Items.Add(new HorizontalRow(new List<(IItem, int)>
            {
                (new ItemIcon(selectedTrade.ItemType, 40), 48),
                (CreateRowLabel(amountAndName, 18), 420)
            }, 40));

            menu.Items.Add(CreateParagraphLabel(rateText, 16));
            menu.Items.Add(CreateParagraphLabel(
                string.Format(
                    T(player, "Maximum possible right now: {0}", "Aktuell maximal moeglich: {0}"),
                    visibleMaxCount),
                16));

            if (maxCount > MaxTradeBatchCount)
            {
                menu.Items.Add(CreateParagraphLabel(
                    string.Format(
                        T(player, "Limited to {0} trades per click for stability.", "Aus Stabilitaetsgruenden auf {0} Handel pro Klick begrenzt."),
                        MaxTradeBatchCount),
                    15));
            }

            JObject tradePayload = new JObject
            {
                { "recipeKey", selectedTrade.Recipe.Key.Index },
                { "isMax", false }
            };
            JObject maxPayload = new JObject
            {
                { "recipeKey", selectedTrade.Recipe.Key.Index },
                { "isMax", true }
            };

            menu.Items.Add(new HorizontalRow(new List<(IItem, int)>
            {
                (CreateRowLabel(T(player, "Quantity", "Menge"), 16), 150),
                (new InputField(StorageQuantity, 150, RowHeight), 150),
                (CreateActionButton(ButtonIdTrade, T(player, "Trade", "Handeln"), tradePayload, 120, visibleMaxCount > 0), 120),
                (CreateActionButton(ButtonIdTrade, T(player, "Trade Max", "Max handeln"), maxPayload, 140, visibleMaxCount > 0), 140)
            }, RowHeight));

            menu.Items.Add(CreateParagraphLabel(
                string.Format(
                    T(player, "Entered quantity: {0}", "Eingegebene Menge: {0}"),
                    quantityText),
                15));
        }

        private static bool TryResolveSelectedTrade(
            List<TradeRecipeView> buyTrades,
            List<TradeRecipeView> sellTrades,
            uint? selectedRecipeKey,
            out TradeRecipeView selectedTrade)
        {
            if (selectedRecipeKey.HasValue)
            {
                if (TryFindTradeByRecipeKey(buyTrades, selectedRecipeKey.Value, out selectedTrade))
                {
                    return true;
                }

                if (TryFindTradeByRecipeKey(sellTrades, selectedRecipeKey.Value, out selectedTrade))
                {
                    return true;
                }
            }

            if (buyTrades.Count > 0)
            {
                selectedTrade = buyTrades[0];
                return true;
            }

            if (sellTrades.Count > 0)
            {
                selectedTrade = sellTrades[0];
                return true;
            }

            selectedTrade = default(TradeRecipeView);
            return false;
        }

        private static bool TryFindTradeByRecipeKey(List<TradeRecipeView> trades, uint recipeKey, out TradeRecipeView trade)
        {
            for (int i = 0; i < trades.Count; i++)
            {
                if (trades[i].Recipe.Key.Index == recipeKey)
                {
                    trade = trades[i];
                    return true;
                }
            }

            trade = default(TradeRecipeView);
            return false;
        }

        private static void CollectTradeRecipes(ColonyGroup group, List<TradeRecipeView> buyTrades, List<TradeRecipeView> sellTrades)
        {
            List<Recipe> pointRecipes = ServerManager.RecipeStorage.PointRecipes;
            for (int i = 0; i < pointRecipes.Count; i++)
            {
                Recipe recipe = pointRecipes[i];
                if (recipe.RequiredResearchCount > 0 && !group.RecipeData.HasUnlocked(recipe.Key))
                {
                    continue;
                }

                if (!TryCreateTradeView(recipe, out TradeRecipeView trade))
                {
                    continue;
                }

                if (trade.IsBuy)
                {
                    buyTrades.Add(trade);
                }
                else
                {
                    sellTrades.Add(trade);
                }
            }
        }

        private static bool TryCreateTradeView(Recipe recipe, out TradeRecipeView trade)
        {
            int pointsCost = 0;
            int pointsGain = 0;
            ushort itemType = 0;
            int itemAmount = 0;

            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                InventoryItem requirement = recipe.Requirements[i];
                if (requirement.Type == s_pointsType)
                {
                    pointsCost += requirement.Amount;
                }
                else if (itemType == 0)
                {
                    itemType = requirement.Type;
                    itemAmount = requirement.Amount;
                }
            }

            for (int i = 0; i < recipe.Results.Count; i++)
            {
                RecipeResult result = recipe.Results[i];
                if (result.Type == s_pointsType)
                {
                    pointsGain += result.Amount;
                }
                else if (itemType == 0)
                {
                    itemType = result.Type;
                    itemAmount = result.Amount;
                }
            }

            bool isBuy = pointsCost > 0 && pointsGain == 0;
            bool isSell = pointsGain > 0 && pointsCost == 0;
            if ((!isBuy && !isSell) || itemType == 0)
            {
                trade = default(TradeRecipeView);
                return false;
            }

            if (itemAmount <= 0)
            {
                itemAmount = 1;
            }

            trade = new TradeRecipeView(
                recipe,
                isBuy,
                itemType,
                itemAmount,
                isBuy ? pointsCost : pointsGain);
            return true;
        }

        private static int GetMaxTradeCount(ColonyGroup group, Recipe recipe)
        {
            long maxCount = int.MaxValue;

            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                InventoryItem requirement = recipe.Requirements[i];
                if (requirement.Amount <= 0)
                {
                    continue;
                }

                long available = requirement.Type == s_pointsType
                    ? group.ColonyPoints
                    : group.Stockpile.AmountContained(requirement.Type);

                if (available < requirement.Amount)
                {
                    return 0;
                }

                maxCount = Math.Min(maxCount, available / requirement.Amount);
            }

            if (maxCount == int.MaxValue)
            {
                maxCount = 0;
            }

            for (int i = 0; i < recipe.Results.Count; i++)
            {
                RecipeResult result = recipe.Results[i];
                if (result.Amount <= 0)
                {
                    continue;
                }

                if (result.Type == s_pointsType)
                {
                    long remainingCapacity = Math.Max(0L, group.ColonyPointsCap - group.ColonyPoints);
                    if (remainingCapacity < result.Amount)
                    {
                        return 0;
                    }

                    maxCount = Math.Min(maxCount, remainingCapacity / result.Amount);
                    continue;
                }

                int currentAmount = group.Stockpile.AmountContained(result.Type);
                long remainingItemCapacity = int.MaxValue - (long)currentAmount;
                if (remainingItemCapacity < result.Amount)
                {
                    return 0;
                }

                maxCount = Math.Min(maxCount, remainingItemCapacity / result.Amount);
            }

            return SafeLongToInt(maxCount);
        }

        private static int GetVisibleMaxTradeCount(ColonyGroup group, Recipe recipe)
        {
            return Math.Min(GetMaxTradeCount(group, recipe), MaxTradeBatchCount);
        }

        private static int ProcessPointRecipeBatch(ColonyGroup group, Recipe recipe, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (recipe.RequiredResearchCount > 0 && !group.RecipeData.HasUnlocked(recipe.Key))
            {
                return 0;
            }

            count = Math.Min(count, Math.Min(GetMaxTradeCount(group, recipe), MaxTradeBatchCount));
            if (count <= 0 || !CanApplyPointRecipeBatch(group, recipe, count))
            {
                return 0;
            }

            if (!RemovePointRecipeRequirements(group, recipe, count))
            {
                return 0;
            }

            AddPointRecipeResults(group, recipe, count);
            return count;
        }

        private static bool CanApplyPointRecipeBatch(ColonyGroup group, Recipe recipe, int count)
        {
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                InventoryItem requirement = recipe.Requirements[i];
                long totalAmount = (long)requirement.Amount * count;
                if (totalAmount <= 0L)
                {
                    continue;
                }

                if (requirement.Type == s_pointsType)
                {
                    if (group.ColonyPoints < totalAmount)
                    {
                        return false;
                    }
                }
                else if (totalAmount > int.MaxValue || group.Stockpile.AmountContained(requirement.Type) < (int)totalAmount)
                {
                    return false;
                }
            }

            for (int i = 0; i < recipe.Results.Count; i++)
            {
                RecipeResult result = recipe.Results[i];
                long totalAmount = (long)result.Amount * count;
                if (totalAmount <= 0L)
                {
                    continue;
                }

                if (result.Type == s_pointsType)
                {
                    if (group.ColonyPointsCap - group.ColonyPoints < totalAmount)
                    {
                        return false;
                    }
                }
                else if (totalAmount > int.MaxValue || int.MaxValue - group.Stockpile.AmountContained(result.Type) < totalAmount)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool RemovePointRecipeRequirements(ColonyGroup group, Recipe recipe, int count)
        {
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                InventoryItem requirement = recipe.Requirements[i];
                long totalAmount = (long)requirement.Amount * count;
                if (totalAmount <= 0L)
                {
                    continue;
                }

                if (requirement.Type == s_pointsType)
                {
                    group.AddColonyPoints(-totalAmount);
                    continue;
                }

                if (totalAmount > int.MaxValue || !group.Stockpile.TryRemove(requirement.Type, (int)totalAmount))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddPointRecipeResults(ColonyGroup group, Recipe recipe, int count)
        {
            for (int i = 0; i < recipe.Results.Count; i++)
            {
                RecipeResult result = recipe.Results[i];
                int resultAmount = GetResultAmount(result, count);
                if (resultAmount <= 0)
                {
                    continue;
                }

                if (result.Type == s_pointsType)
                {
                    group.AddColonyPoints(resultAmount);
                }
                else
                {
                    group.Stockpile.Add(result.Type, resultAmount);
                }
            }
        }

        private static int GetResultAmount(RecipeResult result, int count)
        {
            if (result.Chance >= 1f)
            {
                long totalAmount = (long)result.Amount * count;
                return totalAmount >= int.MaxValue ? int.MaxValue : (int)totalAmount;
            }

            int successfulRolls = 0;
            for (int i = 0; i < count; i++)
            {
                if (Pipliz.Random.NextDouble() < result.Chance)
                {
                    successfulRolls++;
                }
            }

            long randomTotalAmount = (long)result.Amount * successfulRolls;
            return randomTotalAmount >= int.MaxValue ? int.MaxValue : (int)randomTotalAmount;
        }

        private static bool TryReserveTradeSlot(Players.Player player)
        {
            int playerId = player.ID.ID.ID;
            long now = DateTime.UtcNow.Ticks;

            lock (s_tradeLock)
            {
                if (s_nextTradeTicksByPlayer.TryGetValue(playerId, out long nextAllowedTicks) && now < nextAllowedTicks)
                {
                    return false;
                }

                s_nextTradeTicksByPlayer[playerId] = now + TradeCooldownTicks;
                return true;
            }
        }

        private static bool IsReservedPopupClick(PlayerClickedData click)
        {
            if (click == null)
            {
                return false;
            }

            if (click.ClickSource == PlayerClickedData.EClickSource.FirstPerson)
            {
                return click.ClickType == PlayerClickedData.EClickType.Right &&
                       click.ConsumedType == PlayerClickedData.EConsumedType.Reserved;
            }

            if (click.ClickSource == PlayerClickedData.EClickSource.TopDown)
            {
                return click.ClickType == PlayerClickedData.EClickType.Left &&
                       click.ConsumedType == PlayerClickedData.EConsumedType.Reserved;
            }

            return false;
        }

        private static bool TryGetColonyGroup(JObject storage, out ColonyGroup group)
        {
            group = null;
            if (storage == null || !storage.TryGetValue("colonyGroupID", out JToken token))
            {
                return false;
            }

            return ServerManager.ColonyTracker.TryGet(new ColonyGroupID(token.Value<int>()), out group);
        }

        private static string GetQuantityText(JObject storage)
        {
            if (storage != null && storage.TryGetValue(StorageQuantity, out JToken token) && token.Type != JTokenType.Null)
            {
                return token.Value<string>();
            }

            return null;
        }

        private static string NormalizeQuantityText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "1";
            }

            string trimmed = text.Trim();
            return trimmed.Length == 0 ? "1" : trimmed;
        }

        private static bool TryParsePositiveInt(string text, out int value)
        {
            if (int.TryParse(text, out value) && value > 0)
            {
                return true;
            }

            value = 0;
            return false;
        }

        private static int SafeLongToInt(long value)
        {
            if (value <= 0L)
            {
                return 0;
            }

            if (value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)value;
        }

        private static Label CreateParagraphLabel(string text, int fontSize)
        {
            return new Label(new LabelData(text, ELabelAlignment.Default, fontSize, LabelData.ELocalizationType.None));
        }

        private static Label CreateRowLabel(string text, int fontSize)
        {
            return new Label(new LabelData(text, ELabelAlignment.Default, fontSize, LabelData.ELocalizationType.None), RowHeight);
        }

        private static Label CreateCenteredRowLabel(string text, int fontSize)
        {
            return new Label(new LabelData(text, ELabelAlignment.MiddleCenter, fontSize, LabelData.ELocalizationType.None), RowHeight);
        }

        private static ButtonCallback CreateActionButton(string id, string text, JObject payload, int width, bool enabled)
        {
            return new ButtonCallback(
                id,
                new LabelData(text, ELabelAlignment.MiddleCenter, 16, LabelData.ELocalizationType.None),
                width,
                RowHeight,
                ButtonCallback.EOnClickActions.DisableAllInteractive,
                payload,
                0f,
                0f,
                enabled);
        }

        private static string T(Players.Player player, string english, string german)
        {
            string locale = player?.LastKnownLocale;
            if (!string.IsNullOrEmpty(locale) && locale.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            {
                return german;
            }

            return english;
        }

        private readonly struct TradeRecipeView
        {
            public TradeRecipeView(Recipe recipe, bool isBuy, ushort itemType, int itemAmount, int pointsAmount)
            {
                Recipe = recipe;
                IsBuy = isBuy;
                ItemType = itemType;
                ItemAmount = itemAmount;
                PointsAmount = pointsAmount;
            }

            public Recipe Recipe { get; }
            public bool IsBuy { get; }
            public ushort ItemType { get; }
            public int ItemAmount { get; }
            public int PointsAmount { get; }
        }
    }
}
