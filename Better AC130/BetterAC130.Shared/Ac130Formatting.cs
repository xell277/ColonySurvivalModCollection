using System;
using System.Globalization;

namespace BetterAC130.Shared;

public static class Ac130Formatting
{
	public static string FormatSeconds(double seconds)
	{
		return Math.Max(0.0, seconds).ToString("0.0", CultureInfo.InvariantCulture) + "s";
	}

	public static string FormatDays(double days)
	{
		return Math.Max(0.0, days).ToString("0.0", CultureInfo.InvariantCulture) + "d";
	}
}
