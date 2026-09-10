using System.Security.Principal;

namespace CenterConsole.Core.Services;

public interface IElevationService
{
    bool IsElevated { get; }
}

public sealed class ElevationService : IElevationService
{
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
