﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license.

/* This is a derivative from multiple answers on https://stackoverflow.com/questions/3342941/kill-child-process-when-parent-process-is-killed */

/* roa-nyx: This is further a derivative of AndrewArnott's version found at: https://gist.github.com/AArnott/2609636d2f2369495abe76e8a01446a4 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.System.JobObjects;
using static Windows.Win32.PInvoke;

/// <summary>
/// Allows processes to be automatically killed if this parent process unexpectedly quits
/// (or when an instance of this class is disposed).
/// </summary>
namespace AndrewArnott;
public class ProcessJobTracker : IDisposable
{
	private readonly object disposeLock = new object();
	private bool disposed;

	/// <summary>
	/// The job handle.
	/// </summary>
	/// <remarks>
	/// Closing this handle would close all tracked processes. So we don't do it in this process
	/// so that it happens automatically when our process exits.
	/// </remarks>
	private readonly SafeFileHandle jobHandle;

	private static int jobCounter = 0;

	/// <summary>
	/// Initializes a new instance of the <see cref="ProcessJobTracker"/> class.
	/// </summary>
	public unsafe ProcessJobTracker()
	{
		if (!OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
			throw new NotSupportedException("This OS does not support Windows Jobs feature");
		// The job name is optional (and can be null) but it helps with diagnostics.
		//  If it's not null, it has to be unique. Use SysInternals' Handle command-line
		//  utility: handle -a ChildProcessTracker
		lock (disposeLock)
		{
			jobHandle = CreateJobObject(null, nameof(WindowsRideOrDie) + jobCounter++);
		}

		var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
		{
			BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
			{
				LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
			}
		};

		if (!SetInformationJobObject(jobHandle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &extendedInfo, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
		{
			throw new Win32Exception();
		}
	}

	/// <summary>
	/// Ensures a given process is killed when the current process exits.
	/// </summary>
	/// <param name="process">The process whose lifetime should never exceed the lifetime of the current process.</param>
	public void AddProcess(Process process)
	{
#pragma warning disable CA1416
		bool success = AssignProcessToJobObject(jobHandle, new SafeFileHandle(process.Handle, ownsHandle: false));
#pragma warning restore CA1416
		if (!success && !process.HasExited)
			throw new Win32Exception();
	}

	/// <summary>
	/// Kills all processes previously tracked with <see cref="AddProcess(Process)"/> by closing the Windows Job.
	/// </summary>
	public void Dispose()
	{
		lock (disposeLock)
		{
			if (!disposed)
				jobHandle?.Dispose();

			disposed = true;
		}
		GC.SuppressFinalize(this);
	}
}