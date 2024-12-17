using AndrewArnott;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WindowsRideOrDie;
static class Program
{
	private const string MSG_CRIT_PROC_ENDED = "CRIT_PROC_ENDED";
	private const string MSG_RST_PROC_NEEDS_RESTART_EVENTUALLY = "RST_PROC_NEEDS_RESTART_EVENTUALLY";

	private static AutoResetEvent evReady = new AutoResetEvent(false);
	private static Queue<string> messages = new Queue<string>();


	public static void NotifyCriticalProcessEnded()
	{
		invoke(MSG_CRIT_PROC_ENDED);
	}
	public static void NotifyNeedRestartEventually()
	{
		invoke(MSG_RST_PROC_NEEDS_RESTART_EVENTUALLY);
	}

	private static void invoke(string message)
	{
		lock (messages)
		{
			messages.Enqueue(message);
		}
		evReady.Set();
	}

	private static int Main(string[] args)
	{
		if(args.Length == 0)
		{
			Console.Error.WriteLine($"{nameof(WindowsRideOrDie)} [configuration-file-path]");
			return 1;
		}
		if (args.Length != 1)
			throw new ArgumentOutOfRangeException($"Expected a single argument but got {args.Length} instead");

		Console.Error.WriteLine($"Parsing configuration file: {args[0]}");
		IEnumerable<ProcessConfig> configs = ProcessConfig.ConfigurationParser(args[0]);
		if (!configs.Any())
		{
			Console.Error.WriteLine("Configuration contained no commands to run.");
			return 0;
		}

		ProcessJobTracker tracker = new ProcessJobTracker();

		foreach (ProcessConfig config in configs)
			config.Start(tracker);

		DateTime nextRestart = DateTime.MaxValue;

		while (true)
		{
			string msg;
			if (!evReady.WaitOne(nextRestart == DateTime.MaxValue ? TimeSpan.FromMilliseconds(-1) : (nextRestart - DateTime.Now)))
			{
				nextRestart = DateTime.MaxValue;
				msg = MSG_RST_PROC_NEEDS_RESTART_EVENTUALLY;
			}
			else
			{
				lock (messages)
				{
					msg = messages.Dequeue();
				}
			}
			if (msg == MSG_CRIT_PROC_ENDED)
			{
				//Critical process ended, let's notify the console and let the job handle tearing everything down.
				//We won't bother with processing the rest of the messages queue either.
				foreach(ProcessConfig config in configs)
				{
					if (config.ProcessType == ProcessTypes.CRITICAL && config.HasExited)
						Console.Error.WriteLine($"\r\nCritical process ({config}) has ended!");
				}
				break;
			}
			else if (msg == MSG_RST_PROC_NEEDS_RESTART_EVENTUALLY)
			{
				DateTime now = DateTime.Now;
				foreach(ProcessConfig config in configs)
				{
					if (config.ProcessType != ProcessTypes.RESTART)
						continue;
					if(now < config.NextRestart)
					{
						if (config.NextRestart < nextRestart)
							nextRestart = config.NextRestart;
						continue;
					}
					if (config.HasExited)
						config.Start(tracker);
				}

			}
		}

		return 0;
	}
}