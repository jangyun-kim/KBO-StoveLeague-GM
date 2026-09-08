using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 전체 선수 원본 템플릿(PlayerTemplate ScriptableObject 에셋)을 보관하는 싱글톤 DB.
    /// 실제 유저 소유 카드(Models.Player 인스턴스)는 CreatePlayerInstance()로 템플릿을 참조해 발급한다.
    /// </summary>
    public class PlayerDatabase : MonoBehaviour
    {
        public static PlayerDatabase Instance { get; private set; }

        [Tooltip("에디터에서 생성한 PlayerTemplate .asset들을 여기에 드래그하여 등록한다.")]
        [SerializeField] private List<PlayerTemplate> allTemplates = new List<PlayerTemplate>();

        /// <summary>DB에 등록된 전체 선수 템플릿 (읽기 전용 뷰).</summary>
        public IReadOnlyList<PlayerTemplate> AllTemplates => allTemplates;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>TemplateId로 원본 템플릿을 조회한다.</summary>
        public PlayerTemplate GetTemplateById(string templateId)
        {
            return allTemplates.FirstOrDefault(t => t.TemplateId == templateId);
        }

        /// <summary>
        /// 템플릿을 참조하는 새 카드 인스턴스를 발급한다. (스카우트/뽑기 등에서 사용)
        /// </summary>
        public Player CreatePlayerInstance(string templateId)
        {
            var template = GetTemplateById(templateId);
            if (template == null) return null;

            return new Player(System.Guid.NewGuid().ToString(), template);
        }
    }
}
