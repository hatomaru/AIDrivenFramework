using UnityEditor;
using UnityEngine;
using AIDrivenFW.Config;

[CustomPropertyDrawer(typeof(ModelInfoConfig))]
public class AIDriven_ModelInfoConfigDrawer : PropertyDrawer
{
    private const float LineHeight = 18f;
    private const float LineSpacing = 2f;
    private const int VramMin = 0;
    private int VramMax = 65536;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // 折りたたみ状態をチェック
        if (!property.isExpanded)
        {
            return LineHeight;
        }

        // 展開時: ModelName, DownloadUrl, MinVRAM (slider), MaxVRAM (slider), Level = 5行
        return (LineHeight + LineSpacing) * 6 + LineSpacing;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // 折りたたみヘッダー
        var foldoutRect = new Rect(position.x, position.y, position.width, LineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;

            float currentY = position.y + LineHeight + LineSpacing;
            VramMax = AIDrivenConfig.GetVRAM();
            // ModelName
            var modelNameProp = property.FindPropertyRelative("ModelName");
            var modelNameRect = new Rect(position.x, currentY, position.width, LineHeight);
            EditorGUI.PropertyField(modelNameRect, modelNameProp, new GUIContent("Model Name"));
            currentY += LineHeight + LineSpacing;

            // DownloadUrl
            var downloadUrlProp = property.FindPropertyRelative("DownloadUrl");
            var downloadUrlRect = new Rect(position.x, currentY, position.width, LineHeight);
            EditorGUI.PropertyField(downloadUrlRect, downloadUrlProp, new GUIContent("Download URL"));
            currentY += LineHeight + LineSpacing;

            // MinVRAM - スライダー
            var minVramProp = property.FindPropertyRelative("MinVRAM");
            var minVramRect = new Rect(position.x, currentY, position.width, LineHeight);
            minVramProp.intValue = EditorGUI.IntSlider(minVramRect, "Min VRAM (MB)", minVramProp.intValue, VramMin, VramMax);
            currentY += LineHeight + LineSpacing;

            // MaxVRAM - スライダー
            var maxVramProp = property.FindPropertyRelative("MaxVRAM");
            var maxVramRect = new Rect(position.x, currentY, position.width, LineHeight);
            maxVramProp.intValue = EditorGUI.IntSlider(maxVramRect, "Max VRAM (MB)", maxVramProp.intValue, VramMin, VramMax);
            currentY += LineHeight + LineSpacing;

            // MinVRAM が MaxVRAM より大きい場合の警告
            if (minVramProp.intValue > maxVramProp.intValue)
            {
                EditorGUI.HelpBox(new Rect(position.x, currentY, position.width, LineHeight), 
                    "Min VRAM should not exceed Max VRAM", MessageType.Warning);
                currentY += LineHeight + LineSpacing;
            }

            // Level
            var levelProp = property.FindPropertyRelative("Level");
            var levelRect = new Rect(position.x, currentY, position.width, LineHeight);
            EditorGUI.PropertyField(levelRect, levelProp, new GUIContent("Level"));

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }
}
