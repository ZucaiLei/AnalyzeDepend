using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class AnalyzeDependDataWindow : EditorWindow
{
    class DependData
    {
        public Hash128 assetDependencyHash;
        public string[] dependsPath;
    }

    class AtlasDependData
    {
        public string atlasPath;
        public List<UISpriteData> spriteList;
    }

    class AnalysisService
    {
        Dictionary<string, List<string>> _cachedAssetsMap;

        public Dictionary<string, List<string>> GetReferences()
        {
            if (_cachedAssetsMap == null)
            {
                FillReverseDependenciesMap(out _cachedAssetsMap);
            }

            return _cachedAssetsMap;
        }

        void FillReverseDependenciesMap(out Dictionary<string, List<string>> reverseDependencies)
        {
            var assetPaths = AssetDatabase.GetAllAssetPaths().ToList();

            reverseDependencies = assetPaths.ToDictionary(assetPath => assetPath, assetPath => new List<string>());

            Debug.Log($"Total Assets Count: {assetPaths.Count}");

            for (var i = 0; i < assetPaths.Count; i++)
            {
                var pathName = assetPaths[i];
                EditorUtility.DisplayProgressBar("资源收集中", pathName, (float)i / assetPaths.Count);

                var assetDependencies = AssetDatabase.GetDependencies(pathName, false);

                foreach (var assetDependency in assetDependencies)
                {
                    if (reverseDependencies.ContainsKey(assetDependency) && assetDependency != pathName)
                    {
                        reverseDependencies[assetDependency].Add(pathName);
                    }
                }
            }

            EditorUtility.ClearProgressBar();
        }
    }

    string filePath = string.Empty;
    string fileName = string.Empty;

    Vector2 scrollPosition;
    GUIStyle normalStyle;
    GUIStyle greenStyle;
    GUIStyle warningStyle;

    Dictionary<string, DependData> dictDependsCache = new Dictionary<string, DependData>();
    DependData tempAnalyzeCache = null;

    Dictionary<string, List<string>> dictReferenceCache = new Dictionary<string, List<string>>();
    AnalysisService _service;

    Dictionary<string, AtlasDependData> dictAtlasDependCache = new Dictionary<string, AtlasDependData>();

    string diffPath = "Assets/Version/"; //比对原路径，只做展示颜色差异
    string altasPath = "Assets/Fix/Atlas/"; //是否被对应文件夹下图集说关联

    void OnEnable()
    {
        normalStyle = new GUIStyle();
        normalStyle.normal.textColor = Color.white;

        greenStyle = new GUIStyle();
        greenStyle.normal.textColor = Color.green;

        warningStyle = new GUIStyle();
        warningStyle.normal.textColor = Color.red;

        dictDependsCache.Clear();
        tempAnalyzeCache = null;
        dictReferenceCache.Clear();
        _service = null;
    }

    void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        if (null == tempAnalyzeCache)
        {
            EditorGUILayout.HelpBox("未找到关联关系.", MessageType.Warning);
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, false, true);

        if (null != tempAnalyzeCache)
        {
            var content = tempAnalyzeCache.dependsPath.Length > 0 ? $"关联项: {tempAnalyzeCache.dependsPath.Length}" : "未找到关联项";
            EditorGUILayout.LabelField(content);

            var depends = tempAnalyzeCache.dependsPath;
            var alignment = GUI.skin.button.alignment;

            foreach (var resultPath in depends)
            {
                EditorGUILayout.BeginHorizontal();
                var type = AssetDatabase.GetMainAssetTypeAtPath(resultPath);
                if (null != type)
                {
                    bool bCheck = resultPath.Contains(diffPath);
                    GUI.skin.button.alignment = TextAnchor.MiddleLeft;
                    var guiContent = EditorGUIUtility.ObjectContent(null, type);
                    guiContent.text = Path.GetFileName(resultPath);
                    if (GUILayout.Button(guiContent, bCheck ? warningStyle : normalStyle, GUILayout.MinWidth(300f), GUILayout.Height(18f)))
                    {
                        Selection.objects = new[] { AssetDatabase.LoadMainAssetAtPath(resultPath) };
                    }
                }
                else
                {
                    EditorGUILayout.TextField(resultPath, greenStyle);
                }

                EditorGUILayout.EndHorizontal();
            }

            GUI.skin.button.alignment = alignment;
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("分析依赖", GUILayout.Width(160)))
        {
            AnalyzeDependencyDataImp();
        }

        if (GUILayout.Button("分析引用", GUILayout.Width(160)))
        {
            AnalyzeReferencesDataImp();
        }

        if (GUILayout.Button("图集引用", GUILayout.Width(160)))
        {
            AnalyzeAtlasDataImp();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 分析依赖
    /// </summary>
    void AnalyzeDependencyDataImp()
    {
        if (null == Selection.activeObject)
        {
            Debug.LogError("请选择一个资源文件");
            return;
        }

        DependData data = null;
        tempAnalyzeCache = null;
        filePath = AssetDatabase.GetAssetPath(Selection.activeObject);

        if (!dictDependsCache.ContainsKey(filePath))
        {
            var objs = AssetDatabase.GetDependencies(filePath, true);
            data = new DependData();
            data.dependsPath = objs;
            tempAnalyzeCache = data;
            dictDependsCache.Add(filePath, data);
        }
        else
        {
            data = dictDependsCache[filePath];
            var assetHash = AssetDatabase.GetAssetDependencyHash(filePath);
            if (assetHash == data.assetDependencyHash)
            {
                tempAnalyzeCache = data;
            }
            else
            {
                var objs = AssetDatabase.GetDependencies(filePath, true);
                data.dependsPath = objs;
                tempAnalyzeCache = data;
                dictDependsCache[filePath] = data;
            }
        }

        Debug.LogWarning($"{fileName}分析依赖完成");
    }

    /// <summary>
    /// 分析引用(包含图集)
    /// </summary>
    void AnalyzeReferencesDataImp()
    {
        if (null == Selection.activeObject)
        {
            Debug.LogError("请选择一个资源文件");
            return;
        }

        //资源收集
        if (_service == null)
        {
            _service = new AnalysisService();
        }

        dictReferenceCache = _service.GetReferences();
        tempAnalyzeCache = null;

        var path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (dictReferenceCache.ContainsKey(path))
        {
            var lst = dictReferenceCache[path];
            var data = new DependData();
            data.dependsPath = lst.ToArray();

            //图集引用分析
            if (path.EndsWith(".png"))
            {
                if (dictAtlasDependCache.Count <= 0)
                    LoadAtlasDepend();

                var assetName = Path.GetFileNameWithoutExtension(path);
                var value = IsImageUsedByAtlas(assetName);
                if (value.Item1)
                {
                    var index = lst.FindIndex(m => m == value.Item2);
                    if (index == -1)
                    {
                        lst.Add(value.Item2);
                        data.dependsPath = lst.ToArray();
                    }
                }
            }

            tempAnalyzeCache = data;
        }
        else
        {
            Debug.LogWarning("没用引用");
        }

        Debug.LogWarning($"{fileName}分析引用完成");
    }

    /// <summary>
    /// 分析图集引用
    /// </summary>
    void AnalyzeAtlasDataImp()
    {
        if (null == Selection.activeObject)
        {
            Debug.LogError("请选择一个资源文件");
            return;
        }

        tempAnalyzeCache = null;
        filePath = AssetDatabase.GetAssetPath(Selection.activeObject);

        var asset = AssetDatabase.LoadAssetAtPath<NGUIAtlas>(filePath);
        if (asset == null)
        {
            Debug.LogError("类型错误，非图集资源");
            return;
        }

        List<string> temp = new List<string>();
        foreach (var value in asset.spriteList)
        {
            temp.Add(value.name);
        }

        var data = new DependData();
        data.dependsPath = (string[])temp.ToArray();
        tempAnalyzeCache = data;
    }

    /// <summary>
    /// 
    /// </summary>
    void LoadAtlasDepend()
    {
        DirectoryInfo dirDir = new DirectoryInfo(altasPath);
        if (!dirDir.Exists)
            return;

        FileSystemInfo[] fileinfo = dirDir.GetFileSystemInfos();
        if (fileinfo.Length <= 0)
            return;

        foreach (FileSystemInfo info in fileinfo)
        {
            var fullName = info.FullName;

            if (fullName.Contains(".meta"))
                continue;

            if (fullName.EndsWith(".asset"))
            {
                var assetName = Path.GetFileName(fullName);
                var assetPath = $"{altasPath}{assetName}";
                var atlasAsset = AssetDatabase.LoadAssetAtPath<NGUIAtlas>(assetPath);
                if (null != atlasAsset)
                {
                    AtlasDependData tempData = null;
                    if (!dictAtlasDependCache.ContainsKey(assetName))
                    {
                        tempData = new AtlasDependData();
                        dictAtlasDependCache.Add(assetName, tempData);
                    }

                    tempData.atlasPath = assetPath;
                    tempData.spriteList = atlasAsset.spriteList;
                }
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="imageName"></param>
    /// <returns></returns>
    (bool, string) IsImageUsedByAtlas(string imageName)
    {
        if (dictAtlasDependCache.Count > 0)
        {
            foreach (var item in dictAtlasDependCache)
            {
                AtlasDependData spritesItem = item.Value;
                var sprites = spritesItem.spriteList.FindAll(m => m.name == imageName);
                if (sprites.Count > 0)
                {
                    return (sprites.Count > 0, spritesItem.atlasPath);
                }
            }
        }

        return (false, "");
    }


    [MenuItem("Assets/Analyze Depend Data...")]
    static void AnalyzeDependDataImp()
    {
        if (null == Selection.activeObject)
        {
            Debug.LogError("请选择一个资源文件");
            return;
        }

        var window = GetWindow<AnalyzeDependDataWindow>(false, "分析资源(依赖/引用)关系", true);
        window.filePath = AssetDatabase.GetAssetPath(Selection.activeObject);
        window.fileName = Path.GetFileNameWithoutExtension(window.filePath);
        window.AnalyzeDependencyDataImp();
        window.Show();
    }
}