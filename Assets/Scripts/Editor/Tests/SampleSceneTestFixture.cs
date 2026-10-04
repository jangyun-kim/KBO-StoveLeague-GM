using KBOManager.EditorTools;
using NUnit.Framework;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-KBO-184] KBOManager.EditorTests 네임스페이스의 모든 EditMode 테스트가 끝나면 SampleScene을 다시 열어 저장한다 -
    /// 테스트가 남긴 임시 오브젝트/Untitled 씬 대신 에디터가 SampleScene을 마지막 씬으로 기억하게 한다.
    /// </summary>
    [SetUpFixture]
    public class SampleSceneTestFixture
    {
        [OneTimeTearDown]
        public void ReopenSampleScene()
        {
            SampleSceneGuard.EnsureOpen(save: true, forceReopen: true);
        }
    }
}
