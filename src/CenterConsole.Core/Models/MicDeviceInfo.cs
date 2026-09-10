namespace CenterConsole.Core.Models;

/// <summary>An audio capture (microphone) endpoint. Id is the stable MMDevice.ID, safe to persist.</summary>
public sealed record MicDeviceInfo(string Id, string FriendlyName, bool IsDefault);
