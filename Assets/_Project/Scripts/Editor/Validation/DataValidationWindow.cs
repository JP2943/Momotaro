using Momotaro.Data;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Editor.Validation
{
    /// <summary>
    /// プロジェクトの Data 検証を実行し、Error / Warning を一覧表示する EditorWindow（仕様書 11.11）。
    /// メニュー「Momotaro/Validation/Validate Project Data」から開く。
    /// </summary>
    public sealed class DataValidationWindow : EditorWindow
    {
        private DataValidationReport _report;
        private Vector2 _scroll;

        [MenuItem("Momotaro/Validation/Validate Project Data")]
        public static void Open()
        {
            GetWindow<DataValidationWindow>("Data Validation").Show();
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Validate Project Data"))
            {
                _report = ProjectDataValidator.RunAll();
            }

            if (_report == null)
            {
                EditorGUILayout.HelpBox("「Validate Project Data」を押して検証を実行してください。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                $"Errors: {_report.Errors.Count}   Warnings: {_report.Warnings.Count}",
                EditorStyles.boldLabel);

            if (_report.Errors.Count == 0 && _report.Warnings.Count == 0)
            {
                EditorGUILayout.HelpBox("問題は見つかりませんでした。", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (string error in _report.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            foreach (string warning in _report.Warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
