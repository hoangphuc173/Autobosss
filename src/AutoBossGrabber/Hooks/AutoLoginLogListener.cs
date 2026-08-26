using System;
using BepInEx.Logging;

namespace AutoBossGrabber;

/// <summary>
/// Listens to BepInEx log events and triggers auto login reset
/// when a "notingame" message is detected (server kick/disconnect).
/// Ported from NpcScannerPlugin (tool tổng hợp).
/// </summary>
public class AutoLoginLogListener : ILogListener, IDisposable
{
	private AutoLoginController _controller;

	public LogLevel LogLevelFilter => (LogLevel)63; // All levels

	public AutoLoginLogListener(AutoLoginController controller)
	{
		_controller = controller;
	}

	public void LogEvent(object sender, LogEventArgs eventArgs)
	{
		if (eventArgs.Data != null && eventArgs.Data.ToString().ToLower().Contains("notingame"))
		{
			_controller.RequestResetFromLogSignal();
		}
	}

	public void Dispose()
	{
	}
}
