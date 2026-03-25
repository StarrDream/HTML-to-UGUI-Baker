using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Editor.UIBaker
{
    [System.Serializable]
    public class UIDataNode
    {
        public string name;
        public string type;
        public string dir;
        public float value;
        public bool isChecked;
        public List<string> options;
        public float x;
        public float y;
        public float width;
        public float height;
        public string color;
        public string fontColor;
        public int fontSize;
        public string textAlign;
        public string text;
        public List<UIDataNode> children;
    }

    public class HtmlToUGUIBaker : EditorWindow
    {
        private enum InputMode
        {
            FileAsset,
            RawString
        }

        private InputMode currentMode = InputMode.FileAsset;
        private TextAsset jsonAsset;
        private string rawJsonString = "";
        private Vector2 scrollPosition;
        private Canvas targetCanvas;

        // 外部工具链配置
        private string converterUrl = "";
        private const string PREFS_URL_KEY = "HtmlToUGUIBaker_ConverterUrl";

        // 分辨率与 DSL 配置
        private HtmlToUGUIConfig config;
        private int selectedResolutionIndex = 0;
        private const string PREFS_CONFIG_PATH_KEY = "HtmlToUGUIBaker_ConfigPath";
        private const string PREFS_RES_INDEX_KEY = "HtmlToUGUIBaker_ResIndex";

        // 文本组件偏好配置
        private bool useLegacyText = false;
        private const string PREFS_USE_LEGACY_TEXT_KEY = "HtmlToUGUIBaker_UseLegacyText";

        // 用于在当前窗口内嵌绘制 SO 属性的序列化对象
        private SerializedObject configSO;
        private SerializedProperty resolutionsProp;

        [MenuItem("Tools/UI Architecture/HTML to UGUI Baker (Full Controls)")]
        public static void ShowWindow()
        {
            GetWindow<HtmlToUGUIBaker>("UI 原型烘焙器");
        }

        private void OnEnable()
        {
            converterUrl = EditorPrefs.GetString(PREFS_URL_KEY, "");
            string configPath = EditorPrefs.GetString(PREFS_CONFIG_PATH_KEY, "");
            if (!string.IsNullOrEmpty(configPath))
            {
                config = AssetDatabase.LoadAssetAtPath<HtmlToUGUIConfig>(configPath);
            }

            selectedResolutionIndex = EditorPrefs.GetInt(PREFS_RES_INDEX_KEY, 0);
            useLegacyText = EditorPrefs.GetBool(PREFS_USE_LEGACY_TEXT_KEY, false);
        }

        private void OnGUI()
        {
            GUILayout.Label("基于坐标烘焙的 UI 原型生成工具 (支持多分辨率与全控件)", EditorStyles.boldLabel);
            GUILayout.Space(10);

            DrawConfigUI();
            DrawExternalToolchainUI();

            targetCanvas = (Canvas)EditorGUILayout.ObjectField("目标 Canvas", targetCanvas, typeof(Canvas), true);

            GUILayout.Space(5);
            EditorGUI.BeginChangeCheck();
            useLegacyText = EditorGUILayout.Toggle("使用旧版 Text (Legacy)", useLegacyText);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(PREFS_USE_LEGACY_TEXT_KEY, useLegacyText);
            }

            GUILayout.Space(10);
            currentMode = (InputMode)GUILayout.Toolbar((int)currentMode, new string[] { "读取 JSON 文件", "直接粘贴 JSON 字符" });
            GUILayout.Space(10);

            if (currentMode == InputMode.FileAsset)
            {
                DrawFileModeUI();
            }
            else
            {
                DrawStringModeUI();
            }

            GUILayout.Space(20);
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            if (GUILayout.Button("执行烘焙生成", GUILayout.Height(40)))
            {
                ExecuteBake();
            }

            GUI.backgroundColor = Color.white;
        }

        #region UI 绘制逻辑

        private void DrawConfigUI()
        {
            GUILayout.Label("多分辨率与 DSL 配置", EditorStyles.label);
            GUILayout.BeginVertical("box");

            EditorGUI.BeginChangeCheck();
            config = (HtmlToUGUIConfig)EditorGUILayout.ObjectField("配置文件 (SO)", config, typeof(HtmlToUGUIConfig),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                string path = config != null ? AssetDatabase.GetAssetPath(config) : "";
                EditorPrefs.SetString(PREFS_CONFIG_PATH_KEY, path);
                selectedResolutionIndex = 0;
                EditorPrefs.SetInt(PREFS_RES_INDEX_KEY, selectedResolutionIndex);
                configSO = null;
            }

            if (config == null)
            {
                EditorGUILayout.HelpBox(
                    "请先创建并分配 HtmlToUGUIConfig 配置文件。\n(右键 Project 窗口 -> Create -> UI Architecture -> HtmlToUGUI Config)",
                    MessageType.Warning);
                GUILayout.EndVertical();
                GUILayout.Space(10);
                return;
            }

            if (configSO == null || configSO.targetObject != config)
            {
                configSO = new SerializedObject(config);
                resolutionsProp = configSO.FindProperty("supportedResolutions");
            }

            configSO.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(resolutionsProp, new GUIContent("分辨率预设列表 (可自由增删)"), true);
            if (EditorGUI.EndChangeCheck())
            {
                configSO.ApplyModifiedProperties();
            }

            if (config.supportedResolutions == null || config.supportedResolutions.Count == 0)
            {
                EditorGUILayout.HelpBox("配置文件中未定义任何分辨率数据，请点击上方列表的 '+' 号添加。", MessageType.Error);
                GUILayout.EndVertical();
                GUILayout.Space(10);
                return;
            }

            selectedResolutionIndex = Mathf.Clamp(selectedResolutionIndex, 0, config.supportedResolutions.Count - 1);
            string[] resNames = new string[config.supportedResolutions.Count];
            for (int i = 0; i < config.supportedResolutions.Count; i++)
            {
                resNames[i] = config.supportedResolutions[i].displayName;
            }

            EditorGUI.BeginChangeCheck();
            selectedResolutionIndex = EditorGUILayout.Popup("目标分辨率", selectedResolutionIndex, resNames);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(PREFS_RES_INDEX_KEY, selectedResolutionIndex);
            }

            if (GUILayout.Button("复制对应分辨率的 DSL 规范文档", GUILayout.Height(25)))
            {
                CopyDSLToClipboard();
            }

            GUILayout.EndVertical();
            GUILayout.Space(10);
        }

        private void DrawExternalToolchainUI()
        {
            GUILayout.Label("外部工具链桥接", EditorStyles.label);
            GUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PREFS_URL_KEY, converterUrl);
            }

            if (GUILayout.Button("浏览...", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("选择 HTML 转换器", "", "html");
                if (!string.IsNullOrEmpty(path))
                {
                    converterUrl = "file:///" + path.Replace("\\", "/");
                    EditorPrefs.SetString(PREFS_URL_KEY, converterUrl);
                    GUI.FocusControl(null);
                }
            }

            if (GUILayout.Button("在浏览器中打开", GUILayout.Width(120)))
            {
                if (string.IsNullOrWhiteSpace(converterUrl))
                {
                    Debug.LogError("[HtmlToUGUIBaker] 唤起中断: 转换器路径或 URL 为空，请先配置路径或点击浏览选择文件。");
                    return;
                }

                Application.OpenURL(converterUrl);
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawFileModeUI()
        {
            jsonAsset = (TextAsset)EditorGUILayout.ObjectField("JSON 数据源", jsonAsset, typeof(TextAsset), false);
            EditorGUILayout.HelpBox("请将工程目录下的 .json 文件拖拽至此。", MessageType.Info);
        }

        private void DrawStringModeUI()
        {
            GUILayout.Label("在此粘贴 JSON 文本:", EditorStyles.label);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
            rawJsonString = EditorGUILayout.TextArea(rawJsonString, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();
            GUILayout.Space(5);

            if (GUILayout.Button("将当前 JSON 保存为文件到工程目录..."))
            {
                SaveRawJsonToProject();
            }
        }

        #endregion

        #region 核心业务逻辑

        private void CopyDSLToClipboard()
        {
            if (config == null || config.supportedResolutions.Count <= selectedResolutionIndex)
            {
                Debug.LogError("[HtmlToUGUIBaker] 复制失败: 配置文件缺失或分辨率索引越界。");
                return;
            }

            if (config.dslTemplateAsset == null)
            {
                Debug.LogError("[HtmlToUGUIBaker] 复制失败: 配置文件中未指定 DSL 模板文件 (TextAsset)，请在 SO 面板中拖入 .md 模板文件。");
                return;
            }

            Vector2 res = config.supportedResolutions[selectedResolutionIndex].resolution;
            string dsl = config.dslTemplateAsset.text.Replace("{WIDTH}", res.x.ToString())
                .Replace("{HEIGHT}", res.y.ToString());
            GUIUtility.systemCopyBuffer = dsl;
            Debug.Log($"[HtmlToUGUIBaker] 已成功复制分辨率为 {res.x}x{res.y} 的 DSL 规范文档到剪贴板。");
        }

        private void SaveRawJsonToProject()
        {
            if (string.IsNullOrWhiteSpace(rawJsonString))
            {
                Debug.LogError("[HtmlToUGUIBaker] 保存失败: 当前 JSON 字符串为空，请先粘贴数据。");
                return;
            }

            string savePath = EditorUtility.SaveFilePanelInProject(
                "保存 JSON 数据",
                "NewUIWindow.json",
                "json",
                "请选择要保存的目录"
            );

            if (string.IsNullOrEmpty(savePath)) return;

            try
            {
                File.WriteAllText(savePath, rawJsonString);
                AssetDatabase.Refresh();
                TextAsset savedAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(savePath);
                if (savedAsset != null)
                {
                    jsonAsset = savedAsset;
                    currentMode = InputMode.FileAsset;
                    Debug.Log($"[HtmlToUGUIBaker] JSON 文件已成功保存至: {savePath}，并已自动切换至文件模式。");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[HtmlToUGUIBaker] 文件写入失败: 路径 {savePath}，错误信息: {e.Message}");
            }
        }

        private void ExecuteBake()
        {
            if (targetCanvas == null)
            {
                Debug.LogError("[HtmlToUGUIBaker] 烘焙中断: 未指定目标 Canvas，无法确定 UI 挂载点。");
                return;
            }

            string jsonContent = string.Empty;

            if (currentMode == InputMode.FileAsset)
            {
                if (jsonAsset == null)
                {
                    Debug.LogError("[HtmlToUGUIBaker] 烘焙中断: 文件模式下未指定 JSON 数据源。");
                    return;
                }

                jsonContent = jsonAsset.text;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(rawJsonString))
                {
                    Debug.LogError("[HtmlToUGUIBaker] 烘焙中断: 字符串模式下 JSON 内容为空。");
                    return;
                }

                jsonContent = rawJsonString;
            }

            ConfigureCanvasScaler(targetCanvas);

            UIDataNode rootNode = null;
            try
            {
                rootNode = JsonConvert.DeserializeObject<UIDataNode>(jsonContent);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[HtmlToUGUIBaker] 烘焙中断: JSON 解析异常，错误信息: {e.Message}");
                return;
            }

            if (rootNode == null)
            {
                Debug.LogError("[HtmlToUGUIBaker] 烘焙中断: JSON 解析结果为空，请检查数据格式是否符合规范。");
                return;
            }

            GameObject rootGo = CreateUINode(rootNode, targetCanvas.transform, 0f, 0f);
            Undo.RegisterCreatedObjectUndo(rootGo, "Bake UI Prototype");
            Selection.activeGameObject = rootGo;

            Debug.Log($"[HtmlToUGUIBaker] 烘焙完成: 成功生成 UI 树 [{rootGo.name}]，当前基准分辨率已适配。");
        }

        private void ConfigureCanvasScaler(Canvas canvas)
        {
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            Vector2 targetRes = new Vector2(1920, 1080);
            if (config != null && config.supportedResolutions != null &&
                config.supportedResolutions.Count > selectedResolutionIndex)
            {
                targetRes = config.supportedResolutions[selectedResolutionIndex].resolution;
            }

            scaler.referenceResolution = targetRes;
            scaler.matchWidthOrHeight = 0.5f;
        }

        #endregion

        #region 节点生成与组件挂载逻辑

        private GameObject CreateUINode(UIDataNode nodeData, Transform parent, float parentAbsX, float parentAbsY)
        {
            GameObject go = new GameObject(nodeData.name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);

            float localX = nodeData.x - parentAbsX;
            float localY = nodeData.y - parentAbsY;

            rect.anchoredPosition = new Vector2(localX, -localY);
            rect.sizeDelta = new Vector2(nodeData.width, nodeData.height);

            Transform childrenContainer = ApplyComponentByType(go, nodeData);

            if (nodeData.children != null && nodeData.children.Count > 0)
            {
                foreach (var childNode in nodeData.children)
                {
                    CreateUINode(childNode, childrenContainer, nodeData.x, nodeData.y);
                }
            }

            return go;
        }

        private Transform ApplyComponentByType(GameObject go, UIDataNode nodeData)
        {
            Color bgColor = ParseHexColor(nodeData.color, Color.white);
            Color fontColor = ParseHexColor(nodeData.fontColor, Color.black);
            int fontSize = nodeData.fontSize > 0 ? nodeData.fontSize : 24;
            bool isMultiLine = nodeData.height > (fontSize * 1.5f);

            switch (nodeData.type.ToLower())
            {
                case "div":
                case "image":
                    Image img = go.AddComponent<Image>();
                    img.color = bgColor;
                    if (img.color.a <= 0.01f) img.raycastTarget = false;
                    return go.transform;

                case "text":
                    if (useLegacyText)
                    {
                        Text txt = go.AddComponent<Text>();
                        txt.text = nodeData.text;
                        txt.color = fontColor;
                        txt.fontSize = fontSize;
                        txt.alignment = ParseLegacyTextAlign(nodeData.textAlign);
                        txt.horizontalOverflow = isMultiLine ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                        txt.verticalOverflow = isMultiLine ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
                        txt.raycastTarget = false;
                    }
                    else
                    {
                        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
                        txt.text = nodeData.text;
                        txt.color = fontColor;
                        txt.fontSize = fontSize;
                        txt.alignment = ParseTextAlign(nodeData.textAlign);
                        txt.enableWordWrapping = isMultiLine;
                        txt.overflowMode = isMultiLine ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;
                        txt.raycastTarget = false;
                    }

                    return go.transform;

                case "button":
                    Image btnImg = go.AddComponent<Image>();
                    btnImg.color = bgColor;
                    Button btn = go.AddComponent<Button>();
                    btn.targetGraphic = btnImg;

                    GameObject btnTxtGo = CreateChildRect(go, useLegacyText ? "Text" : "Text (TMP)", Vector2.zero,
                        Vector2.one);
                    if (useLegacyText)
                    {
                        Text btnTxt = btnTxtGo.AddComponent<Text>();
                        btnTxt.text = nodeData.text;
                        btnTxt.color = fontColor;
                        btnTxt.fontSize = fontSize;
                        btnTxt.alignment = ParseLegacyTextAlign(nodeData.textAlign);
                        btnTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                        btnTxt.verticalOverflow = VerticalWrapMode.Overflow;
                        btnTxt.raycastTarget = false;
                    }
                    else
                    {
                        TextMeshProUGUI btnTxt = btnTxtGo.AddComponent<TextMeshProUGUI>();
                        btnTxt.text = nodeData.text;
                        btnTxt.color = fontColor;
                        btnTxt.fontSize = fontSize;
                        btnTxt.alignment = ParseTextAlign(nodeData.textAlign);
                        btnTxt.enableWordWrapping = false;
                        btnTxt.overflowMode = TextOverflowModes.Overflow;
                        btnTxt.raycastTarget = false;
                    }

                    return go.transform;

                case "input":
                    Image inputBg = go.AddComponent<Image>();
                    inputBg.color = bgColor;

                    GameObject textAreaGo = CreateChildRect(go, "Text Area", Vector2.zero, Vector2.one,
                        new Vector2(10, 5), new Vector2(-10, -5));
                    textAreaGo.AddComponent<RectMask2D>();

                    GameObject phGo = CreateChildRect(textAreaGo, "Placeholder", Vector2.zero, Vector2.one);
                    GameObject textGo = CreateChildRect(textAreaGo, "Text", Vector2.zero, Vector2.one);

                    Color phColor = fontColor;
                    phColor.a = 0.5f;

                    if (useLegacyText)
                    {
                        InputField inputField = go.AddComponent<InputField>();
                        inputField.targetGraphic = inputBg;

                        Text phTxt = phGo.AddComponent<Text>();
                        phTxt.text = nodeData.text;
                        phTxt.color = phColor;
                        phTxt.fontSize = fontSize;
                        phTxt.alignment = ParseLegacyTextAlign(nodeData.textAlign);
                        phTxt.raycastTarget = false;

                        Text inTxt = textGo.AddComponent<Text>();
                        inTxt.color = fontColor;
                        inTxt.fontSize = fontSize;
                        inTxt.alignment = ParseLegacyTextAlign(nodeData.textAlign);
                        inTxt.raycastTarget = false;

                        inputField.textComponent = inTxt;
                        inputField.placeholder = phTxt;
                    }
                    else
                    {
                        TMP_InputField inputField = go.AddComponent<TMP_InputField>();
                        inputField.targetGraphic = inputBg;

                        TextMeshProUGUI phTxt = phGo.AddComponent<TextMeshProUGUI>();
                        phTxt.text = nodeData.text;
                        phTxt.color = phColor;
                        phTxt.fontSize = fontSize;
                        phTxt.alignment = ParseTextAlign(nodeData.textAlign);
                        phTxt.enableWordWrapping = false;
                        phTxt.raycastTarget = false;

                        TextMeshProUGUI inTxt = textGo.AddComponent<TextMeshProUGUI>();
                        inTxt.color = fontColor;
                        inTxt.fontSize = fontSize;
                        inTxt.alignment = ParseTextAlign(nodeData.textAlign);
                        inTxt.enableWordWrapping = false;
                        inTxt.raycastTarget = false;

                        inputField.textViewport = textAreaGo.GetComponent<RectTransform>();
                        inputField.textComponent = inTxt;
                        inputField.placeholder = phTxt;
                    }

                    return go.transform;

                case "scroll":
                    Image scrollBg = go.AddComponent<Image>();
                    scrollBg.color = bgColor;
                    if (scrollBg.color.a <= 0.01f) scrollBg.raycastTarget = false;

                    ScrollRect scrollRect = go.AddComponent<ScrollRect>();
                    bool isVertical = string.IsNullOrEmpty(nodeData.dir) || nodeData.dir.ToLower() == "v";
                    scrollRect.horizontal = !isVertical;
                    scrollRect.vertical = isVertical;

                    GameObject viewportGo = CreateChildRect(go, "Viewport", Vector2.zero, Vector2.one);
                    viewportGo.AddComponent<RectMask2D>();

                    GameObject contentGo = CreateChildRect(viewportGo, "Content", new Vector2(0, 1), new Vector2(0, 1));
                    RectTransform contentRect = contentGo.GetComponent<RectTransform>();
                    contentRect.pivot = new Vector2(0, 1);
                    contentRect.sizeDelta = new Vector2(nodeData.width, nodeData.height);

                    scrollRect.viewport = viewportGo.GetComponent<RectTransform>();
                    scrollRect.content = contentRect;
                    return contentGo.transform;

                case "toggle":
                    Toggle toggle = go.AddComponent<Toggle>();
                    toggle.isOn = nodeData.isChecked;

                    float boxSize = Mathf.Min(nodeData.height, 30f);
                    GameObject tBgGo = CreateChildRect(go, "Background", new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                    RectTransform tBgRect = tBgGo.GetComponent<RectTransform>();
                    tBgRect.sizeDelta = new Vector2(boxSize, boxSize);
                    tBgRect.anchoredPosition = new Vector2(boxSize / 2, 0);
                    Image tBgImg = tBgGo.AddComponent<Image>();
                    tBgImg.color = Color.white;

                    GameObject checkGo = CreateChildRect(tBgGo, "Checkmark", Vector2.zero, Vector2.one);
                    Image checkImg = checkGo.AddComponent<Image>();
                    checkImg.color = Color.black;
                    RectTransform checkRect = checkGo.GetComponent<RectTransform>();
                    checkRect.offsetMin = new Vector2(4, 4);
                    checkRect.offsetMax = new Vector2(-4, -4);

                    GameObject tLblGo = CreateChildRect(go, "Label", Vector2.zero, Vector2.one);
                    RectTransform tLblRect = tLblGo.GetComponent<RectTransform>();
                    tLblRect.offsetMin = new Vector2(boxSize + 10, 0);

                    if (useLegacyText)
                    {
                        Text tLblTxt = tLblGo.AddComponent<Text>();
                        tLblTxt.text = nodeData.text;
                        tLblTxt.color = fontColor;
                        tLblTxt.fontSize = fontSize;
                        tLblTxt.alignment = TextAnchor.MiddleLeft;
                    }
                    else
                    {
                        TextMeshProUGUI tLblTxt = tLblGo.AddComponent<TextMeshProUGUI>();
                        tLblTxt.text = nodeData.text;
                        tLblTxt.color = fontColor;
                        tLblTxt.fontSize = fontSize;
                        tLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                        tLblTxt.enableWordWrapping = false;
                    }

                    toggle.targetGraphic = tBgImg;
                    toggle.graphic = checkImg;
                    return go.transform;

                case "slider":
                    Slider slider = go.AddComponent<Slider>();
                    slider.value = Mathf.Clamp01(nodeData.value);

                    GameObject sBgGo = CreateChildRect(go, "Background", new Vector2(0, 0.25f), new Vector2(1, 0.75f));
                    Image sBgImg = sBgGo.AddComponent<Image>();
                    sBgImg.color = bgColor;

                    GameObject fillAreaGo = CreateChildRect(go, "Fill Area", Vector2.zero, Vector2.one,
                        new Vector2(5, 0), new Vector2(-15, 0));
                    GameObject fillGo = CreateChildRect(fillAreaGo, "Fill", Vector2.zero, Vector2.one);
                    Image fillImg = fillGo.AddComponent<Image>();
                    fillImg.color = fontColor;

                    GameObject handleAreaGo = CreateChildRect(go, "Handle Slide Area", Vector2.zero, Vector2.one,
                        new Vector2(10, 0), new Vector2(-10, 0));
                    GameObject handleGo = CreateChildRect(handleAreaGo, "Handle", Vector2.zero, Vector2.one);
                    RectTransform handleRect = handleGo.GetComponent<RectTransform>();
                    handleRect.sizeDelta = new Vector2(20, 0);
                    Image handleImg = handleGo.AddComponent<Image>();
                    handleImg.color = Color.white;

                    slider.targetGraphic = handleImg;
                    slider.fillRect = fillGo.GetComponent<RectTransform>();
                    slider.handleRect = handleRect;
                    return go.transform;

                case "dropdown":
                    Image dBgImg = go.AddComponent<Image>();
                    dBgImg.color = bgColor;

                    GameObject dLblGo = CreateChildRect(go, "Label", Vector2.zero, Vector2.one, new Vector2(10, 0),
                        new Vector2(-30, 0));
                    GameObject arrowGo = CreateChildRect(go, "Arrow", new Vector2(1, 0.5f), new Vector2(1, 0.5f));
                    RectTransform arrowRect = arrowGo.GetComponent<RectTransform>();
                    arrowRect.sizeDelta = new Vector2(20, 20);
                    arrowRect.anchoredPosition = new Vector2(-15, 0);
                    Image arrowImg = arrowGo.AddComponent<Image>();
                    arrowImg.color = fontColor;

                    GameObject templateGo = CreateChildRect(go, "Template", new Vector2(0, 0), new Vector2(1, 0));
                    RectTransform templateRect = templateGo.GetComponent<RectTransform>();
                    templateRect.pivot = new Vector2(0.5f, 1);
                    templateRect.sizeDelta = new Vector2(0, 150);
                    templateRect.anchoredPosition = new Vector2(0, -2);
                    Image tempImg = templateGo.AddComponent<Image>();
                    tempImg.color = Color.white;

                    ScrollRect tempScroll = templateGo.AddComponent<ScrollRect>();
                    tempScroll.horizontal = false;
                    tempScroll.vertical = true;
                    templateGo.SetActive(false);

                    GameObject dViewportGo = CreateChildRect(templateGo, "Viewport", Vector2.zero, Vector2.one);
                    dViewportGo.AddComponent<Image>().color = Color.white;
                    dViewportGo.AddComponent<Mask>();

                    GameObject dContentGo =
                        CreateChildRect(dViewportGo, "Content", new Vector2(0, 1), new Vector2(1, 1));
                    RectTransform dContentRect = dContentGo.GetComponent<RectTransform>();
                    dContentRect.pivot = new Vector2(0.5f, 1);
                    dContentRect.sizeDelta = new Vector2(0, 28);

                    GameObject itemGo = CreateChildRect(dContentGo, "Item", new Vector2(0, 0.5f), new Vector2(1, 0.5f));
                    RectTransform itemRect = itemGo.GetComponent<RectTransform>();
                    itemRect.sizeDelta = new Vector2(0, 28);
                    Toggle itemToggle = itemGo.AddComponent<Toggle>();

                    GameObject itemBgGo = CreateChildRect(itemGo, "Item Background", Vector2.zero, Vector2.one);
                    Image itemBgImg = itemBgGo.AddComponent<Image>();
                    itemBgImg.color = Color.white;

                    GameObject itemCheckGo = CreateChildRect(itemGo, "Item Checkmark", new Vector2(0, 0.5f),
                        new Vector2(0, 0.5f));
                    RectTransform itemCheckRect = itemCheckGo.GetComponent<RectTransform>();
                    itemCheckRect.sizeDelta = new Vector2(20, 20);
                    itemCheckRect.anchoredPosition = new Vector2(15, 0);
                    Image itemCheckImg = itemCheckGo.AddComponent<Image>();
                    itemCheckImg.color = Color.black;

                    GameObject itemLblGo = CreateChildRect(itemGo, "Item Label", Vector2.zero, Vector2.one,
                        new Vector2(30, 0), new Vector2(-10, 0));

                    itemToggle.targetGraphic = itemBgImg;
                    itemToggle.graphic = itemCheckImg;
                    tempScroll.viewport = dViewportGo.GetComponent<RectTransform>();
                    tempScroll.content = dContentRect;

                    if (useLegacyText)
                    {
                        Dropdown dropdown = go.AddComponent<Dropdown>();

                        Text dLblTxt = dLblGo.AddComponent<Text>();
                        dLblTxt.color = fontColor;
                        dLblTxt.fontSize = fontSize;
                        dLblTxt.alignment = TextAnchor.MiddleLeft;

                        Text itemLblTxt = itemLblGo.AddComponent<Text>();
                        itemLblTxt.color = Color.black;
                        itemLblTxt.fontSize = fontSize;
                        itemLblTxt.alignment = TextAnchor.MiddleLeft;

                        dropdown.targetGraphic = dBgImg;
                        dropdown.template = templateRect;
                        dropdown.captionText = dLblTxt;
                        dropdown.itemText = itemLblTxt;

                        if (nodeData.options != null && nodeData.options.Count > 0)
                        {
                            dropdown.ClearOptions();
                            List<Dropdown.OptionData> optList = new List<Dropdown.OptionData>();
                            foreach (var opt in nodeData.options) optList.Add(new Dropdown.OptionData(opt));
                            dropdown.AddOptions(optList);
                        }
                    }
                    else
                    {
                        TMP_Dropdown dropdown = go.AddComponent<TMP_Dropdown>();

                        TextMeshProUGUI dLblTxt = dLblGo.AddComponent<TextMeshProUGUI>();
                        dLblTxt.color = fontColor;
                        dLblTxt.fontSize = fontSize;
                        dLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                        dLblTxt.enableWordWrapping = false;

                        TextMeshProUGUI itemLblTxt = itemLblGo.AddComponent<TextMeshProUGUI>();
                        itemLblTxt.color = Color.black;
                        itemLblTxt.fontSize = fontSize;
                        itemLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                        itemLblTxt.enableWordWrapping = false;

                        dropdown.targetGraphic = dBgImg;
                        dropdown.template = templateRect;
                        dropdown.captionText = dLblTxt;
                        dropdown.itemText = itemLblTxt;

                        if (nodeData.options != null && nodeData.options.Count > 0)
                        {
                            dropdown.ClearOptions();
                            List<TMP_Dropdown.OptionData> optList = new List<TMP_Dropdown.OptionData>();
                            foreach (var opt in nodeData.options) optList.Add(new TMP_Dropdown.OptionData(opt));
                            dropdown.AddOptions(optList);
                        }
                    }

                    return go.transform;

                default:
                    Debug.LogWarning($"[HtmlToUGUIBaker] 未知节点类型: {nodeData.type}");
                    return go.transform;
            }
        }

        private TextAlignmentOptions ParseTextAlign(string alignStr)
        {
            if (string.IsNullOrEmpty(alignStr)) return TextAlignmentOptions.Midline;
            switch (alignStr.ToLower())
            {
                case "left":
                case "start":
                    return TextAlignmentOptions.MidlineLeft;
                case "right":
                case "end":
                    return TextAlignmentOptions.MidlineRight;
                case "center":
                default:
                    return TextAlignmentOptions.Midline;
            }
        }

        private TextAnchor ParseLegacyTextAlign(string alignStr)
        {
            if (string.IsNullOrEmpty(alignStr)) return TextAnchor.MiddleCenter;
            switch (alignStr.ToLower())
            {
                case "left":
                case "start":
                    return TextAnchor.MiddleLeft;
                case "right":
                case "end":
                    return TextAnchor.MiddleRight;
                case "center":
                default:
                    return TextAnchor.MiddleCenter;
            }
        }

        private GameObject CreateChildRect(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin ?? Vector2.zero;
            rect.offsetMax = offsetMax ?? Vector2.zero;
            return go;
        }

        private Color ParseHexColor(string hex, Color defaultColor)
        {
            if (string.IsNullOrEmpty(hex)) return defaultColor;
            if (ColorUtility.TryParseHtmlString(hex, out Color color)) return color;
            return defaultColor;
        }

        #endregion
    }
}