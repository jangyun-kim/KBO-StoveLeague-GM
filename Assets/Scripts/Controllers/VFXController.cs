using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 과금/육성 순간의 시각 연출(Game Feel)을 전담하는 싱글톤. 전부 순수 Unity Coroutine +
    /// Mathf.Lerp/AnimationCurve로만 구현했다 - 프로젝트 manifest.json에 DOTween/LeanTween 설치가
    /// 확인되지 않아, 없는 패키지에 기대는 대신 엔진 기본 기능만으로 완결되게 만들었다(과제 원문이
    /// "DOTween, LeanTween 또는 Unity 애니메이터" 중 택일을 허용했으므로 이 선택은 그 범위 안이다).
    ///
    /// 이 클래스는 어떤 카드가 어떤 등급인지, 강화가 왜 성공/실패했는지 전혀 모른다 - 오직 "이 GameObject를
    /// 이렇게 움직여라"만 받는 순수 연출 계층이다. 호출부(ShopUIController/InventoryUIController 등)가
    /// 무엇을 왜 연출할지 결정한다.
    /// </summary>
    public class VFXController : MonoBehaviour
    {
        public static VFXController Instance { get; private set; }

        [Header("Premium Ten-Pull Reveal")]
        [SerializeField] private float cardRevealStagger = 0.2f;
        [SerializeField] private float cardPopDuration = 0.25f;
        [SerializeField] private AnimationCurve popCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Camera Shake (확정 카드 등장 시)")]
        [Tooltip("비워두면 카메라 흔들림을 생략한다.")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float shakeDuration = 0.3f;
        [SerializeField] private float shakeMagnitude = 0.15f;

        [Header("Guaranteed Card Particle (뼈대 - 실제 파티클 리소스는 아트 준비 후 연결)")]
        [SerializeField] private ParticleSystem guaranteedCardParticle;

        [Header("Enhance Feedback")]
        [SerializeField] private float punchScaleAmount = 1.25f;
        [SerializeField] private float punchDuration = 0.35f;
        [SerializeField] private float failShakeDuration = 0.35f;
        [SerializeField] private float failShakeMagnitude = 12f; // 픽셀 단위 좌우 흔들림
        [SerializeField] private Color enhanceFailFlashColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private float enhanceFailFlashDuration = 0.25f;
        [SerializeField] private Color enhanceSuccessFlashColor = Color.white;

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

        // ----- 프리미엄 10연뽑 결과창 -----

        /// <summary>
        /// cardRoots를 0.2초 간격으로 팝업/페이드인시킨다(각 카드는 CanvasGroup이 있으면 알파도 함께
        /// 페이드인 - 없으면 스케일 팝만 적용). guaranteedCardIndex 카드가 등장하는 순간에는 카메라
        /// 흔들림 + 파티클을 함께 터뜨린다. 논블로킹: 이 메서드는 코루틴을 등록만 하고 즉시 반환하며,
        /// 전체 연출이 끝나면 onComplete가 호출된다 - 호출자(ShopUIController 등)는 이 메서드 호출 뒤
        /// 곧바로 다음 UI 작업(예: 재화 텍스트 갱신)을 계속 진행해도 된다.
        /// </summary>
        public void PlayPremiumTenPullReveal(IReadOnlyList<GameObject> cardRoots, int guaranteedCardIndex, Action onComplete = null)
        {
            StartCoroutine(RevealSequence(cardRoots, guaranteedCardIndex, onComplete));
        }

        private IEnumerator RevealSequence(IReadOnlyList<GameObject> cardRoots, int guaranteedCardIndex, Action onComplete)
        {
            if (cardRoots != null)
            {
                for (int i = 0; i < cardRoots.Count; i++)
                {
                    var card = cardRoots[i];
                    if (card != null)
                    {
                        card.SetActive(true);
                        yield return StartCoroutine(PopIn(card));

                        if (i == guaranteedCardIndex)
                        {
                            yield return StartCoroutine(ShakeCamera());
                            if (guaranteedCardParticle != null) guaranteedCardParticle.Play();
                        }
                    }

                    // 마지막 카드 뒤에는 굳이 더 기다릴 필요가 없다.
                    if (i < cardRoots.Count - 1)
                    {
                        yield return new WaitForSeconds(cardRevealStagger);
                    }
                }
            }

            onComplete?.Invoke();
        }

        private IEnumerator PopIn(GameObject card)
        {
            var t = card.transform;
            var canvasGroup = card.GetComponent<CanvasGroup>();

            t.localScale = Vector3.zero;
            if (canvasGroup != null) canvasGroup.alpha = 0f;

            float elapsed = 0f;
            while (elapsed < cardPopDuration)
            {
                elapsed += Time.deltaTime;
                float progress = popCurve.Evaluate(Mathf.Clamp01(elapsed / cardPopDuration));
                t.localScale = Vector3.one * progress;
                if (canvasGroup != null) canvasGroup.alpha = progress;
                yield return null;
            }

            t.localScale = Vector3.one;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }

        private IEnumerator ShakeCamera()
        {
            if (cameraTransform == null) yield break;

            var originalPos = cameraTransform.localPosition;
            float elapsed = 0f;

            while (elapsed < shakeDuration)
            {
                elapsed += Time.deltaTime;
                float damper = 1f - Mathf.Clamp01(elapsed / shakeDuration);
                Vector2 offset = UnityEngine.Random.insideUnitCircle * shakeMagnitude * damper;
                cameraTransform.localPosition = originalPos + new Vector3(offset.x, offset.y, 0f);
                yield return null;
            }

            cameraTransform.localPosition = originalPos;
        }

        // ----- 강화 성공/실패 -----

        /// <summary>강화 성공 연출: 카드 배경(또는 테두리) 이미지가 흰색으로 짧게 반짝이고, 강화 수치
        /// 텍스트가 커졌다 작아지는 펀치 스케일을 재생한다. 둘 중 하나가 null이면 그 부분만 생략한다.</summary>
        public void PlayEnhanceSuccess(Image cardGlowImage, Transform reinforceLevelTransform)
        {
            if (cardGlowImage != null) StartCoroutine(FlashColor(cardGlowImage, enhanceSuccessFlashColor, punchDuration));
            if (reinforceLevelTransform != null) StartCoroutine(PunchScale(reinforceLevelTransform, punchScaleAmount, punchDuration));
        }

        /// <summary>강화 실패 연출: 카드가 좌우로 흔들리고 배경이 회색으로 짧게 번쩍인다.</summary>
        public void PlayEnhanceFailure(Transform cardTransform, Image cardBackgroundImage)
        {
            if (cardTransform != null) StartCoroutine(ShakeTransform(cardTransform, failShakeDuration, failShakeMagnitude));
            if (cardBackgroundImage != null) StartCoroutine(FlashColor(cardBackgroundImage, enhanceFailFlashColor, enhanceFailFlashDuration));
        }

        private IEnumerator PunchScale(Transform target, float peakScale, float duration)
        {
            var original = target.localScale;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // 0~0.5: 1배 -> peakScale로 커짐, 0.5~1: peakScale -> 1배로 되돌아옴 (삼각 곡선 펀치).
                float scaleFactor = t < 0.5f
                    ? Mathf.Lerp(1f, peakScale, t / 0.5f)
                    : Mathf.Lerp(peakScale, 1f, (t - 0.5f) / 0.5f);
                target.localScale = original * scaleFactor;
                yield return null;
            }

            target.localScale = original;
        }

        private IEnumerator ShakeTransform(Transform target, float duration, float magnitudePixels)
        {
            var original = target.localPosition;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float damper = 1f - Mathf.Clamp01(elapsed / duration);
                float offsetX = UnityEngine.Random.Range(-1f, 1f) * magnitudePixels * damper;
                target.localPosition = original + new Vector3(offsetX, 0f, 0f);
                yield return null;
            }

            target.localPosition = original;
        }

        private IEnumerator FlashColor(Image image, Color flashColor, float duration)
        {
            var original = image.color;
            float half = Mathf.Max(0.01f, duration / 2f);

            float elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                image.color = Color.Lerp(original, flashColor, elapsed / half);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                image.color = Color.Lerp(flashColor, original, elapsed / half);
                yield return null;
            }

            image.color = original;
        }
    }
}
