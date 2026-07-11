using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Pipliz;
using Chatting;
using Chatting.Commands;
using Pipliz.Threading;
using Newtonsoft.Json;

namespace ColonyCommands
{

	public class AnnouncementConfig
	{
		public string welcomeMessage { get; set; }
		public int IntervalSeconds { get; set; }
		public List<AnnouncementMessage> Messages { get; set; }
	}


	public class AnnouncementMessage
	{
		public string Text { get; set; }
		public bool Enabled { get; set; }
	}


	public static class Announcements
	{
		public static int MIN_INTERVAL = 10;
		public static AnnouncementConfig config = new AnnouncementConfig();
		public static int CurrentIndex;
		static int IntervalCounter;

		static string ConfigFilePath {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, "announcements.json"));
			}
		}


		public static void AfterWorldLoad()
		{
			if (!File.Exists(ConfigFilePath)) {
				config.welcomeMessage = "";
				config.IntervalSeconds = MIN_INTERVAL * 60;
				config.Messages = new List<AnnouncementMessage>();
			} else {
				Load();
			}
			SendNextAnnouncement();
		}


		public static void OnPlayerConnectedLate(Players.Player player)
		{
			if (config.welcomeMessage.Length > 0) {
				Chat.Send(player, config.welcomeMessage);
			}
		}


		public static void SendNextAnnouncement()
		{
			try {
				IntervalCounter += MIN_INTERVAL;
				if (IntervalCounter >= config.IntervalSeconds) {
					IntervalCounter = 0;
					if (config.Messages.Count > 0) {
						for (var c = CurrentIndex; c < CurrentIndex + config.Messages.Count; c++) {
							int Index = c % config.Messages.Count;
							AnnouncementMessage Message = config.Messages[Index];
							if (Message.Enabled && Message.Text.Length > 0) {
								CurrentIndex = Index;
								Chat.SendToConnected(Message.Text);
								break;
							}
						}
						CurrentIndex = (CurrentIndex + 1) % config.Messages.Count;
					}
				}
			} catch (Exception exception) {
				Log.WriteError($"Exception while sending announcement: {exception.Message}");
			}
			ThreadManager.InvokeOnMainThread(delegate() { SendNextAnnouncement(); }, MIN_INTERVAL);
		}


		public static void Load()
		{
			try {
				JsonSerializer js = new JsonSerializer();
				JsonTextReader jtr = new JsonTextReader(new StreamReader(ConfigFilePath));
				config = js.Deserialize<AnnouncementConfig>(jtr);
				jtr.Close();
				Log.Write ($"Using announcements interval {config.IntervalSeconds} seconds");
				Log.Write ($"Loaded {config.Messages.Count} announcments from file");
			} catch (Exception exception) {
				Log.WriteError($"Error loading announcements: {exception.Message}");
			}
		}


		static void Save()
		{
			try {
				Log.Write($"Saving {ConfigFilePath}");
				JsonSerializer json = new JsonSerializer();
				JsonTextWriter jsonWriter = new JsonTextWriter(new StreamWriter(ConfigFilePath));
				json.Formatting = Formatting.Indented;
				json.Serialize(jsonWriter, config);
				jsonWriter.Close();
			} catch (Exception exception) {
				Log.WriteError($"Error saving announcements: {exception.Message}");
			}
		}


		public static void SetIntervalSeconds(int seconds)
		{
			config.IntervalSeconds = System.Math.Max(seconds, MIN_INTERVAL);
			Save();
		}


		public static string ListAllAnnouncements()
		{
			string result = "";
			for (var c = 0; c < config.Messages.Count; c++) {
				AnnouncementMessage Message = config.Messages[c];
				result += string.Format("A{0} ({1}): {2}\n", c, Message.Enabled ? "Enabled" : "Disabled", Message.Text);
			}
			return result;
		}


		public static string ListEnabledAnnouncements()
		{
			string result = "";
			for (var c = 0; c < config.Messages.Count; c++) {
				AnnouncementMessage Message = config.Messages[c];
				if (Message.Enabled) {
					result += string.Format("A{0} ({1}): {2}\n", c, Message.Enabled ? "Enabled" : "Disabled", Message.Text);
				}
			}
			return result;
		}


		public static void AddAnnouncement(string text)
		{
			AnnouncementMessage msg = new AnnouncementMessage();
			msg.Enabled = true;
			msg.Text = text;
			config.Messages.Insert(CurrentIndex, msg);
			Save();
		}


		public static void RemoveAnnouncement(int index)
		{
			config.Messages.RemoveAt(index);
			Save();
		}


		public static void ChangeAnnouncement(int index, string text)
		{
			config.Messages[index].Text = text;
			Save();
		}


		public static void MoveAnnouncement(int index, int newIndex)
		{
			AnnouncementMessage msg = config.Messages[index];
			config.Messages.RemoveAt(index);
			config.Messages.Insert(newIndex, msg);
			Save();
		}


		public static void EnableAnnouncement(int index)
		{
			config.Messages[index].Enabled = true;
			Save();
		}


		public static void EnableAllAnnouncements()
		{
			config.Messages.ForEach(message => message.Enabled = true);
			Save();
		}


		public static void DisableAnnouncement(int index)
		{
			config.Messages[index].Enabled = false;
			Save();
		}


		public static void DisableAllAnnouncements()
		{
			config.Messages.ForEach(message => message.Enabled = false);
			Save();
		}


		public static void SetWelcomeMessage(string text)
		{
			config.welcomeMessage = text;
			Save();
		}

	}
}

