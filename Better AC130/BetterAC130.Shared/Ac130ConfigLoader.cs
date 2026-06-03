using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace BetterAC130.Shared;

public static class Ac130ConfigLoader
{
	private static readonly Dictionary<string, Ac130Config> Cache = new Dictionary<string, Ac130Config>(StringComparer.OrdinalIgnoreCase);

	public static Ac130Config LoadFromAssemblyPath(string assemblyPath)
	{
		return LoadFromModRoot(Path.GetDirectoryName(assemblyPath));
	}

	public static Ac130Config LoadFromModRoot(string modRoot)
	{
		if (string.IsNullOrEmpty(modRoot))
		{
			throw new ArgumentException("Mod root path cannot be null or empty.", "modRoot");
		}
		if (Cache.TryGetValue(modRoot, out var value))
		{
			return value;
		}
		string text = Path.Combine(modRoot, "ac130config.json");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("BetterAC130 config file was not found.", text);
		}
		value = JsonConvert.DeserializeObject<Ac130Config>(File.ReadAllText(text)) ?? new Ac130Config();
		Cache[modRoot] = value;
		return value;
	}
}
