using System.Reflection;

namespace SDVXEditor.Resources;

/// <summary>
/// Anchor type for <c>new DllResourceStore(SDVXEditorResources.ResourceAssembly)</c>.
/// </summary>
public static class SDVXEditorResources
{
    public static Assembly ResourceAssembly => typeof(SDVXEditorResources).Assembly;
}
