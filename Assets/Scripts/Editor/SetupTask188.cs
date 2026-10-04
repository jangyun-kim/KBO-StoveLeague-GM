using System.IO;
using System.Linq;
using System.Text;
using KBOManager.UI;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-188] 씬/리소스 적용(idempotent).
    ///   1) 초상화 색인 Resources/Portraits/portrait_index.txt 재생성 - PortraitResolver가 카드 고유 초상화가 없을 때 같은 선수의
    ///      다른 등급·연도 초상화로 폴백한다(Resources는 런타임 폴더 목록을 줄 수 없으므로 에디터가 색인을 만든다).
    /// 선수 카드 3단 레이아웃 · 각성 배지 · 성장 센터 대상 2줄 · 중계 타순표 열 분리 · 경기 진행 방식(빠른 진행 N경기 / 하이라이트 / 풀 플레이)은
    /// 런타임 코드(PlayerCardUI.Setup, GrowthCenterView/CompyaMatchView.Build)라 씬 저장값과 무관하게 적용된다
    /// (CompyaMatchView · GrowthCenterView 계층은 SetupCompyaMatchUI179 / SetupTask184가 같은 Build()로 다시 만든다).
    /// </summary>
    public static class SetupTask188
    {
        public const string PortraitFolder = "Assets/Resources/Portraits";
        public const string PortraitIndexPath = PortraitFolder + "/portrait_index.txt";

        [MenuItem("KBO Manager/Setup/Apply TASK-188 (Portrait Index)")]
        public static void ApplyAll()
        {
            int count = BuildPortraitIndex();
            Debug.Log($"[SetupTask188] TASK-188 적용 완료(초상화 색인 {count}장) - 씬을 저장(Ctrl+S)하십시오.");
        }

        /// <summary>Portraits 폴더의 png를 "Portraits/{팀}/{연도}/{TemplateId}"(확장자 없음) 한 줄씩 색인한다(_BG 듀얼 샷 포함 - 해석기가 거른다).</summary>
        public static int BuildPortraitIndex()
        {
            if (!Directory.Exists(PortraitFolder)) return 0;
            string resourcesRoot = Path.GetFullPath("Assets/Resources").Replace('\\', '/').TrimEnd('/') + "/";
            var lines = Directory.GetFiles(PortraitFolder, "*.png", SearchOption.AllDirectories)
                .Select(f => Path.GetFullPath(f).Replace('\\', '/'))
                .Where(f => f.StartsWith(resourcesRoot))
                .Select(f => f.Substring(resourcesRoot.Length))
                .Select(f => f.Substring(0, f.Length - ".png".Length))
                .OrderBy(f => f, System.StringComparer.Ordinal)
                .ToList();
            string content = string.Join("\n", lines) + "\n";
            string existing = File.Exists(PortraitIndexPath) ? File.ReadAllText(PortraitIndexPath) : null;
            if (existing != content)
            {
                File.WriteAllText(PortraitIndexPath, content, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(PortraitIndexPath, ImportAssetOptions.ForceSynchronousImport);
            }
            PortraitResolver.ResetCache();
            return lines.Count;
        }
    }
}
