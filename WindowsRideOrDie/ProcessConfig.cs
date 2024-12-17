using AndrewArnott;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WindowsRideOrDie;

class ProcessConfig
{
	private static readonly Regex RX_DECLARATION_OR_COMMANDLINE = new(@"^(?:-(?<unkey>[^=]+)|\+(?<key>[^=]+)=(?<value>.*)|(?<cmd>[^+-].*))$");
	private static readonly Dictionary<string, string> DEFAULT_DECLARATIONS = new() {
		{"PROCESS_TYPE","CRITICAL"},
		{"RESTART_DELAY_MS","0"}
	};
	private static readonly string[] executableExtensionsInPriorityOrder = [".exe",".com",".bat",".cmd"];

	public static IEnumerable<ProcessConfig> ConfigurationParser(string configurationFilePath)
	{
		DEFAULT_DECLARATIONS.Add("CWD", Path.GetFullPath(Path.GetDirectoryName(configurationFilePath)!));

		IEnumerator<string> enumerator = ((IEnumerable<string>)File.ReadAllLines(configurationFilePath)).GetEnumerator();

		if (!enumerator.MoveNext())
			return Array.Empty<ProcessConfig>();

		if (enumerator.Current != "ALPHA")
			throw new NotSupportedException($"Configuration file \"{configurationFilePath}\" is reporting version \"{enumerator.Current}\" which is not supported.");

		int lineNumber = 1;

		Dictionary<string, string> currentDeclarations = new Dictionary<string, string>(DEFAULT_DECLARATIONS);

		List<ProcessConfig> processConfigs = new List<ProcessConfig>();

		while (enumerator.MoveNext())
		{
			lineNumber++;
			string line = enumerator.Current.Trim();
			if (line.Length == 0 || line.StartsWith('#'))
				continue;

			Match m = RX_DECLARATION_OR_COMMANDLINE.Match(line);
			if (!m.Success)
				throw new FormatException($"Configuration file \"{configurationFilePath}\" line {lineNumber} has an unexpected format: {line}");

			if (m.Groups["cmd"].Success)
			{
				processConfigs.Add(new ProcessConfig(m.Groups["cmd"].Value, currentDeclarations));
			}
			else
			{
				if (m.Groups["unkey"].Success)
				{
					string key = m.Groups["unkey"].Value;
					currentDeclarations.Remove(key);
					if (DEFAULT_DECLARATIONS.TryGetValue(key, out string? value))
						currentDeclarations.Add(key, value);
				}
				if (m.Groups["key"].Success)
				{
					string key = m.Groups["key"].Value;
					string value = m.Groups["value"].Value;
					if(key == "CWD")
						value = Path.GetFullPath(value, currentDeclarations["CWD"]);
					currentDeclarations[key] = value;
				}
			}
		}

		return processConfigs;
	}

	private static int findCommandArgsDivider(string cmd)
	{
		bool inQuotes = false;
		for(int x = 0; x < cmd.Length - 1; x++)
		{
			if (cmd[x] == ' ' && !inQuotes)
				return x;
			if (cmd[x] == '"')
				inQuotes = !inQuotes;
		}
		return -1;
	}


	private readonly string cmd;
	private readonly string args;
	private readonly string cwd;
	public readonly ProcessTypes ProcessType;
	private readonly TimeSpan restartDelay;
	public DateTime NextRestart { get; private set; } = DateTime.MinValue;
	private Process? p;
	public ProcessConfig(string cmd, Dictionary<string, string> config)
	{
		if (cmd.Length == 0)
			throw new ArgumentOutOfRangeException("Process config command can not be empty");

		cwd = config["CWD"];

		this.cmd = cmd;

		int cmdArgsDivider = findCommandArgsDivider(cmd);
		if (cmdArgsDivider > -1)
		{
			this.cmd = cmd.Substring(0, cmdArgsDivider);
			args = cmd.Substring(cmdArgsDivider + 1).Trim();
		}
		else
		{
			args = "";
		}

		this.cmd = findCmd();


		if (!Enum.TryParse(config["PROCESS_TYPE"], false, out ProcessType))
			throw new ArgumentOutOfRangeException($"Could not convert {config["PROCESS_TYPE"]} to a valid process type");

		restartDelay = TimeSpan.FromMilliseconds(int.Parse(config["RESTART_DELAY_MS"]));
	}

	private string findCmd()
	{
		string cmd = Path.GetFullPath(this.cmd, cwd);
		if (executableExtensionsInPriorityOrder.Any(e => cmd.EndsWith(e)) && File.Exists(cmd))
			return cmd;

		foreach(string ext in executableExtensionsInPriorityOrder)
		{
			string cmdext = cmd + ext;
			if (File.Exists(cmdext))
				return cmdext;
		}

		cmd = this.cmd;

		using Process p = new Process();
		p.StartInfo = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(@"%windir%\System32\where.exe"), cmd);
		p.StartInfo.UseShellExecute = false;
		p.StartInfo.RedirectStandardOutput = true;
		p.Start();
		while (true)
		{
			string? outp = p.StandardOutput.ReadLine();
			if (outp is null)
				throw new ArgumentOutOfRangeException($"Could not find command: {cmd}");
			outp = outp.Trim();
			if (outp.Length == 0)
				continue;
			if (outp.EndsWith(".cmd") || outp.EndsWith(".bat") || outp.EndsWith(".exe") || outp.EndsWith(".com"))
				return outp;
		}
	}

	public void Start(ProcessJobTracker tracker)
	{
		if (p is not null && !p.HasExited)
			throw new NotSupportedException("ProcessConfig does not support starting multiple processes for a single config instance");
		if (p is not null && ProcessType == ProcessTypes.CRITICAL)
			throw new NotSupportedException("ProcessConfig does not support restarting critical processes");
		if (NextRestart > DateTime.Now)
			return;

		p = new Process();
		p.StartInfo = new ProcessStartInfo(cmd, args);
		p.StartInfo.CreateNoWindow = false;
		p.StartInfo.UseShellExecute = false;
		p.StartInfo.WorkingDirectory = cwd;
		p.EnableRaisingEvents = true;

		p.Exited += P_Exited;

		p.Start();
		tracker.AddProcess(p);
	}

	public bool HasExited => p is null ? false : p.HasExited;

	private void P_Exited(object? sender, EventArgs e)
	{
		if (ProcessType == ProcessTypes.CRITICAL)
			Program.NotifyCriticalProcessEnded();
		else if (ProcessType == ProcessTypes.RESTART)
		{
			NextRestart = DateTime.Now + restartDelay;
			Program.NotifyNeedRestartEventually();
		}
	}

	public override string ToString() => $"{cmd} {args}";
}
