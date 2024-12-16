# Windows Ride or Die

This project does not yet work as intended. This readme gives a high-level spec of how it should work in the future.

Simple app that triggers other processes outlined in a configuration file.

However the subprocesses triggered are set up in a Windows Job, and configures the job such that if a critical process ends (expectedly or not) the other processes are cleaned up.

## Getting started

Clone and build this project. Note the path to the built EXE, or copy the built folder somewhere convenient.

Create a configuration.

Run the exe passing the configuration file to it.

> WindowsRideOrDie.exe configuration.jobs

### Sample configuration

Make a file, name it whatever you want and configure it using the following sample.

> WhateverYouWant.jobs

```
ALPHA
# First line in the file must always be a version indicator.

# Example critical process
cmd /K "echo Something important is happening here"

# Change current working directory. All declarations start with "+" and applies to all processes past this declaration until a new declaration overrides it.
# Note that the default CWD is relative to the configuration file triggered.
+CWD=..\frontend
npm install

# Change CWD back to it's default value
-CWD

# Process Types are one of:
# - CRITICAL (default)
# - RESTART - a process that is automatically restarted once it's detected to have ended as long as the critical process(es) are still active.
+PROCESS_TYPE=RESTART
# For PROCESS_TYPE=RESTART, the delay in milliseconds from the time that the process crash is detected until the process is restarted (default is 0)
+RESTART_DELAY_MS=5

npm run dev
```
