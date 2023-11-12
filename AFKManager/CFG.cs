using CounterStrikeSharp.API.Modules.Utils;
using System.Reflection;
using System.Text.Json;

namespace AFKManager;

internal class CFG
{
	public static Config config = new();

    public void CheckConfig(string moduleDirectory)
	{
		string path = Path.Join(moduleDirectory, "config.json");

		if (!File.Exists(path))
		{
			CreateAndWriteFile(path);
		}

		using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
		using (StreamReader sr = new StreamReader(fs))
		{
			// Deserialize the JSON from the file and load the configuration.
			config = JsonSerializer.Deserialize<Config>(sr.ReadToEnd());
		}

		if (config != null)
		{
			if(config.ChatPrefix != null)
				config.ChatPrefix = ModifyColorValue(config.ChatPrefix);

			if(config.ChatMoveMessage != null)
				config.ChatMoveMessage = ModifyColorValue(config.ChatMoveMessage);

			if(config.ChatKickMessage != null)
				config.ChatKickMessage = ModifyColorValue(config.ChatKickMessage);

			if(config.ChatKillMessage != null)
				config.ChatKillMessage = ModifyColorValue(config.ChatKillMessage);

			if(config.ChatWarningKillMessage != null)
				config.ChatWarningKillMessage = ModifyColorValue(config.ChatWarningKillMessage);

			if(config.ChatWarningMoveMessage != null)
				config.ChatWarningMoveMessage = ModifyColorValue(config.ChatWarningMoveMessage);

			if(config.ChatWarningKickMessage != null)
				config.ChatWarningKickMessage = ModifyColorValue(config.ChatWarningKickMessage);

            if (config.Punishment < 0 || config.Punishment > 2)
            {
                config.Punishment = 1;
                Console.WriteLine("AFK Manager: Punishment value is invalid, setting to default value (1).");
            }

			if(config.Timer < 0.1f)
			{                 
				config.Timer = 5.0f;
				Console.WriteLine("AFK Manager: Timer value is invalid, setting to default value (5.0).");
			}

			if(config.SpecWarnPlayerEveryXSeconds < config.Timer)
			{
				config.SpecWarnPlayerEveryXSeconds = config.Timer;
				Console.WriteLine($"AFK Manager: The value of SpecWarnPlayerEveryXSeconds is less than the value of Timer, SpecWarnPlayerEveryXSeconds will be forced to {config.Timer}");
            }
        }
	}

	private static void CreateAndWriteFile(string path)
	{

		using (FileStream fs = File.Create(path))
		{
			// File is created, and fs will automatically be disposed when the using block exits.
		}

		Console.WriteLine($"File created: {File.Exists(path)}");

		Config config = new Config
		{
			// create dictionary with default values
			WhiteListUsers = new Dictionary<ulong, Whitelist>()
			{
				{ 76561198143759075, new Whitelist { SkipAFK = true, SkipSPEC = false } }
			},
			ChatPrefix = "[{LightRed}AFK{Default}]",
			ChatMoveMessage = "{chatprefix} {playername} was moved to SPEC being AFK.",
			ChatKillMessage = "{chatprefix} {playername} was killed for being AFK.",
			ChatKickMessage = "{chatprefix} {playername} was kicked for being AFK.",
			ChatWarningKickMessage = "{chatprefix} You\'re{LightRed} Idle/ AFK{Default}. Move or you\'ll be kicked in {Darkred}{time}{Default} seconds.",
			ChatWarningMoveMessage = "{chatprefix} You\'re{LightRed} Idle/ AFK{Default}. Move or you\'ll be moved to SPEC in {Darkred}{time}{Default} seconds.",
			ChatWarningKillMessage = "{chatprefix} You\'re{LightRed} Idle/ AFK{Default}. Move or you\'ll killed in {Darkred}{time}{Default} seconds.",
			SpecWarnPlayerEveryXSeconds = 20.0f,
			SpecKickPlayerAfterXWarnings = 5,
            Warnings = 3,
			Punishment = 1,
			SpecKickMinPlayers = 5,
			Timer = 5.0f,
            Offset = 89
		};

		// Serialize the config object to JSON and write it to the file.
		string jsonConfig = JsonSerializer.Serialize(config, new JsonSerializerOptions()
		{
			WriteIndented = true
		});
		File.WriteAllText(path, jsonConfig);
	}

	// Essential method for replacing chat colors from the config file, the method can be used for other things as well.
	private string ModifyColorValue(string msg)
	{
		if (msg.Contains('{'))
		{
			string modifiedValue = msg;
			foreach (FieldInfo field in typeof(ChatColors).GetFields())
			{
				string pattern = $"{{{field.Name}}}";
				if (msg.Contains(pattern, StringComparison.OrdinalIgnoreCase))
				{
					modifiedValue = modifiedValue.Replace(pattern, field.GetValue(null).ToString(), StringComparison.OrdinalIgnoreCase);
				}
			}
			return modifiedValue;
		}

		return string.IsNullOrEmpty(msg) ? "[AFK]" : msg;
	}
}

internal class Config
{
	public Dictionary<ulong, Whitelist> WhiteListUsers { get; set; }
	public string? ChatPrefix { get; set; }
	public string? ChatKickMessage { get; set; }
	public string? ChatMoveMessage { get; set; }
	public string? ChatKillMessage { get; set; }
	public string? ChatWarningKickMessage { get; set; }
	public string? ChatWarningMoveMessage { get; set; }
	public string? ChatWarningKillMessage { get; set; }

    public int Warnings { get; set; }
    public int Punishment { get; set; }
	public float SpecWarnPlayerEveryXSeconds { get; set; }
	public int SpecKickPlayerAfterXWarnings { get; set; }
	public int SpecKickMinPlayers { get; set; }
	public float Timer { get; set; }
    public int Offset { get; set; }
}
public class Whitelist
{
    public bool SkipAFK { get; set; }
    public bool SkipSPEC { get; set; }
}