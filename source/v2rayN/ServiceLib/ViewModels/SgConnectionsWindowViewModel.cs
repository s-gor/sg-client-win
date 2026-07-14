namespace ServiceLib.ViewModels;

/// <summary>
/// Dedicated inert view-model marker for the SG Connections window.
/// The window owns its refresh lifecycle explicitly and must not inherit the
/// legacy ClashConnectionsViewModel background polling loop.
/// </summary>
public sealed class SgConnectionsWindowViewModel
{
}
