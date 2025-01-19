using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;



public class UnbloatProject : EditorWindow
{
    private List<string> unusedDirectories = new List<string>();
    private long totalUnusedSize = 0; // Tamaño total de los directorios no utilizados
    private Vector2 scrollPos;

    [MenuItem("Tools/Unbloat Project")]
    public static void ShowWindow()
    {
        GetWindow<UnbloatProject>("Unbloat Project");
    }

    private void OnGUI()
    {
        GUILayout.Label("Unbloat Project", EditorStyles.boldLabel);

        if (GUILayout.Button("Analyze Project"))
        {
            AnalyzeProject();
        }

        if (unusedDirectories.Count > 0)
        {
            GUILayout.Label("Unused Directories Found:", EditorStyles.boldLabel);

            GUILayout.Label($"Total Unused Size: {FormatBytes(totalUnusedSize)}", EditorStyles.helpBox);

            if (GUILayout.Button("Delete All", GUILayout.Height(25)))
            {
                DeleteAllDirectories();
            }

            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));

            foreach (string dir in new List<string>(unusedDirectories))
            {
                GUILayout.BeginHorizontal();

                if (GUILayout.Button(dir, EditorStyles.linkLabel))
                {
                    ShowInProjectWindow(dir);
                }

                if (GUILayout.Button("Delete", GUILayout.Width(70)))
                {
                    DeleteDirectory(dir);
                }

                if (GUILayout.Button("Skip", GUILayout.Width(70)))
                {
                    SkipDirectory(dir);
                }

                // Botón para verificar referencias
                if (GUILayout.Button("Check References", GUILayout.Width(120)))
                {
                    CheckReferencesInBuildScenes(dir);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }
        else if (GUILayout.Button("Clear Results"))
        {
            unusedDirectories.Clear();
            totalUnusedSize = 0;
        }
    }

    private void AnalyzeProject()
    {
        unusedDirectories.Clear();
        totalUnusedSize = 0; // Reiniciar el tamaño acumulado

        // Recorrer todos los subdirectorios en "Assets"
        string[] directories = Directory.GetDirectories("Assets", "*", SearchOption.AllDirectories);

        foreach (string directory in directories)
        {
            if (IsDirectoryUnused(directory))
            {
                unusedDirectories.Add(directory);
                totalUnusedSize += CalculateDirectorySize(directory);
            }
        }

        if (unusedDirectories.Count == 0)
        {
            EditorUtility.DisplayDialog("Unbloat Project", "No unused directories found!", "OK");
        }
        else
        {
            Debug.Log($"Found {unusedDirectories.Count} unused directories, totaling {FormatBytes(totalUnusedSize)}.");
        }
    }

    private bool IsDirectoryUnused(string directory)
    {
        string[] files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);

        foreach (string file in files)
        {
            string assetPath = file.Replace("\\", "/");

            // Si el archivo tiene referencias, el directorio está en uso
            string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
            if (dependencies.Length > 1) // Más de 1 porque incluye el archivo en sí mismo
            {
                return false;
            }
        }

        return true;
    }

    private long CalculateDirectorySize(string directory)
    {
        long size = 0;

        // Calcular el tamaño de todos los archivos en el directorio
        string[] files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);

        foreach (string file in files)
        {
            FileInfo fileInfo = new FileInfo(file);
            size += fileInfo.Length;
        }

        return size;
    }

    private void DeleteDirectory(string directory)
    {
        if (EditorUtility.DisplayDialog("Delete Directory", $"Are you sure you want to delete {directory}?", "Yes", "No"))
        {
            long dirSize = CalculateDirectorySize(directory);
            Directory.Delete(directory, true);
            File.Delete(directory + ".meta");
            AssetDatabase.Refresh();
            unusedDirectories.Remove(directory);
            totalUnusedSize -= dirSize; // Restar el tamaño del directorio eliminado
        }
    }

    private void SkipDirectory(string directory)
    {
        unusedDirectories.Remove(directory);
    }

    private void DeleteAllDirectories()
    {
        if (EditorUtility.DisplayDialog("Delete All", "Are you sure you want to delete all unused directories?", "Yes", "No"))
        {
            foreach (string dir in new List<string>(unusedDirectories))
            {
                long dirSize = CalculateDirectorySize(dir);
                Directory.Delete(dir, true);
                File.Delete(dir + ".meta");
                unusedDirectories.Remove(dir);
                totalUnusedSize -= dirSize;
            }

            AssetDatabase.Refresh();
        }
    }

    private void ShowInProjectWindow(string directory)
    {
        string relativePath = directory.Replace("\\", "/");
        Object asset = AssetDatabase.LoadAssetAtPath<Object>(relativePath);
        if (asset != null)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
        else
        {
            Debug.LogWarning($"Could not find directory: {relativePath}");
        }
    }

    private string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000)
        {
            return $"{bytes / 1_000_000_000f:0.##} GB";
        }
        else if (bytes >= 1_000_000)
        {
            return $"{bytes / 1_000_000f:0.##} MB";
        }
        else if (bytes >= 1_000)
        {
            return $"{bytes / 1_000f:0.##} KB";
        }
        else
        {
            return $"{bytes} Bytes";
        }
    }

    // Método para verificar si algún archivo de un directorio está referenciado en las escenas activas
    private void CheckReferencesInBuildScenes(string directory)
    {
        List<string> foundReferences = new List<string>();

        // Revisar todos los archivos dentro del directorio
        string[] files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);

        foreach (string file in files)
        {
            string assetPath = file.Replace("\\", "/");

            // Revisar todas las escenas habilitadas para el build
            foreach (var buildScene in EditorBuildSettings.scenes)
            {
                if (buildScene.enabled)
                {
                    // Cargar la escena en memoria sin mostrarla
                    UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(buildScene.path, UnityEditor.SceneManagement.OpenSceneMode.Additive);

                    // Buscar referencias a este asset en las variables públicas de los MonoBehaviour
                    foreach (GameObject go in scene.GetRootGameObjects())
                    {
                        var components = go.GetComponentsInChildren<MonoBehaviour>(true);
                        foreach (var comp in components)
                        {
                            if (comp != null)
                            {
                                SerializedObject serializedObject = new SerializedObject(comp);
                                SerializedProperty prop = serializedObject.GetIterator();
                                while (prop.NextVisible(true))
                                {
                                    if (prop.propertyType == SerializedPropertyType.ObjectReference)
                                    {
                                        if (prop.objectReferenceValue != null)
                                        {
                                            string referencedAssetPath = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                                            if (referencedAssetPath == assetPath)
                                            {
                                                foundReferences.Add($"Scene: {buildScene.path}, GameObject: {go.name}, Component: {comp.GetType().Name}, Property: {prop.name}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Cerrar la escena para liberar recursos
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        // Mostrar los resultados
        if (foundReferences.Count > 0)
        {
            string message = "Found references in the following scenes:\n\n" + string.Join("\n", foundReferences);
            EditorUtility.DisplayDialog("References Found", message, "OK");

            // Eliminar el directorio de la lista, ya que está en uso
            unusedDirectories.Remove(directory);
            totalUnusedSize -= CalculateDirectorySize(directory); // Restar el tamaño del directorio
        }
        else
        {
            EditorUtility.DisplayDialog("No References Found", "No references were found in any build scene.", "OK");
        }
    }
}
