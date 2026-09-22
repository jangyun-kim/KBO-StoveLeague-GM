using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-167, 사용자 직접 지시 - 4단계 로드맵 "폰트 적용"] KBO 공식 서체 "Dia Gothic"
    /// 3종(Bold/Medium/Light, 사용자가 직접 내려받아 제공한 TTF)을 씬 조립 에디터 스크립트에서
    /// 일관되게 불러오기 위한 헬퍼. `Assets/Fonts/KBODiaGothic/`에 임포트해 두고
    /// `AssetDatabase.LoadAssetAtPath`로 참조한다(런타임 `Resources.Load`가 아니다 - 이 폰트는
    /// Setup*.cs가 에디터에서 씬을 조립하는 시점에만 필요하고, 한 번 조립된 UI는 그 결과를 씬에
    /// 그대로 저장하므로 빌드/런타임에 다시 로드할 필요가 없다).
    ///
    /// [사실 정정] 기존에는 50곳(16개 Setup*.cs 파일)이 전부
    /// `Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf")`(유니티 기본 내장 서체)를
    /// 개별적으로 하드코딩하고 있었다 - 이번 작업에서 그 50곳을 전부 `KBOFonts.Default`로
    /// 교체했다(기계적 일괄 치환, 우변 표현식만 바꿔 좌변 변수명(`text`/`label` 등)과는 무관).
    /// 앞으로 폰트를 바꿀 일이 있으면 이 클래스 하나만 고치면 된다.
    /// </summary>
    public static class KBOFonts
    {
        private const string BasePath = "Assets/Fonts/KBODiaGothic/";

        public static Font Bold => AssetDatabase.LoadAssetAtPath<Font>(BasePath + "KBODiaGothic-Bold.ttf");
        public static Font Medium => AssetDatabase.LoadAssetAtPath<Font>(BasePath + "KBODiaGothic-Medium.ttf");
        public static Font Light => AssetDatabase.LoadAssetAtPath<Font>(BasePath + "KBODiaGothic-Light.ttf");

        /// <summary>대부분의 UI 텍스트가 지금까지 `LegacyRuntime.ttf`를 쓰던 자리를 그대로 대체하는
        /// 기본 본문 서체 - 굵기가 딱 중간이라 제목/본문 어디에도 무난한 `Medium`을 기본값으로
        /// 삼았다. 만에 하나 이 폰트 임포트에 실패해도(예: 사용자가 아직 파일을 내려받지 않은
        /// 새 환경) `null`을 반환할 뿐 예외를 던지지 않으므로, 호출부의 `text.font = KBOFonts.Default;`
        /// 대입 자체는 항상 안전하다 - 다만 그 경우 유니티가 텍스트를 기본 서체로 표시한다.</summary>
        public static Font Default => Medium;

        /// <summary>
        /// [TASK-KBO-167] `KBOFonts.Default`로 바꾼 것은 Setup*.cs가 앞으로 **새로** 만드는 Text에만
        /// 적용된다 - 이 코드베이스의 확립된 "Find-or-Create" 관례상 대부분의 헬퍼가 이미 존재하는
        /// Text는 `existing != null`이면 그 즉시 재사용하고 반환해(폰트 재대입 줄까지 도달하지 않음)
        /// 이미 조립돼 있는 라이브 씬의 기존 UI는 Setup 메뉴를 다시 실행해도 서체가 바뀌지 않는다.
        /// 이 메뉴는 그 간극을 메운다 - 현재 열려 있는 씬 전체를 스캔해(비활성 오브젝트 포함) 모든
        /// `UnityEngine.UI.Text` 컴포넌트의 `font`를 무조건 `KBOFonts.Default`로 덮어쓴다. 특정 Text만
        /// 의도적으로 다른 서체를 쓰도록 예외 처리해 둔 곳은 이 세션이 읽은 범위에서는 없었다(전부
        /// LegacyRuntime.ttf 기본값을 공유) - 그런 예외가 실제로 있다면 이 메뉴 실행 후 Git diff로
        /// 확인해 되돌릴 것.
        /// </summary>
        [MenuItem("KBO Manager/Setup/Apply KBO Dia Gothic Font To Scene")]
        public static void ApplyFontToScene()
        {
            var font = Default;
            if (font == null)
            {
                Debug.LogError("[KBOFonts] KBO Dia Gothic 폰트 에셋을 'Assets/Fonts/KBODiaGothic/'에서 " +
                    "찾지 못했습니다. TTF 파일이 프로젝트에 임포트돼 있는지 확인하십시오.");
                return;
            }

            var allTexts = Resources.FindObjectsOfTypeAll<Text>();
            int appliedCount = 0;
            foreach (var text in allTexts)
            {
                // FindObjectsOfTypeAll은 씬 오브젝트뿐 아니라 프로젝트 에셋(프리팹 원본, 임시 오브젝트
                // 등)까지 전부 훑으므로, 실제로 "현재 열린 씬에 배치된" 인스턴스만 골라낸다(명령서
                // 경계 조건 - 프리팹 에셋 자체를 건드리지 않기 위함).
                if (text == null || !text.gameObject.scene.IsValid()) continue;

                text.font = font;
                EditorUtility.SetDirty(text);
                appliedCount++;
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[KBOFonts] 현재 씬의 Text 컴포넌트 {appliedCount}개에 KBO Dia Gothic 서체를 적용했습니다.");
        }
    }
}
