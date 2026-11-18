#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Strips invalid or missing analyzers inserted into generated .csproj files.
/// </summary>
public class ProjectPostprocessor : AssetPostprocessor
{
    private static string OnGeneratedCSProject(string path, string content)
    {
        if (content.Contains("visualstudiotoolsforunity.vstuc-1.3.2"))
        {
            content = content.Replace(
                "<Analyzer Include=\"C:\\Users\\alexh\\.vscode\\extensions\\visualstudiotoolsforunity.vstuc-1.3.2\\Analyzers\\Microsoft.Unity.Analyzers.dll\" />\r\n",
                ""
            );
            content = content.Replace(
                "<Analyzer Include=\"C:\\Users\\alexh\\.vscode\\extensions\\visualstudiotoolsforunity.vstuc-1.3.2\\Analyzers\\Microsoft.Unity.Analyzers.dll\" />\n",
                ""
            );
        }

        return content;
    }
}
#endif
