using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

    /// <summary>
    /// A lightweight, coroutine-based tweening system for Unity UI.
    /// Does not require external packages. Focuses on bouncy, elastic animations.
    /// </summary>
    public static class UITweenEngine
    {
        // Dictionary to track active tweens by target object to allow cancellation.
        private static readonly Dictionary<object, Coroutine> activeTweens = new Dictionary<object, Coroutine>();

        #region Easing Functions

        /// <summary>
        /// Elastic easing out function. Gives a punchy, bouncy feel with overshoot and quick settle.
        /// </summary>
        private static float EaseElasticOut(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return 1f - Mathf.Cos(t * Mathf.PI * 2.5f) * Mathf.Exp(-6f * t);
        }

        #endregion

        #region Internal Runner
        
        /// <summary>
        /// Hidden MonoBehaviour to run coroutines.
        /// </summary>
        private class UITweenRunner : MonoBehaviour
        {
            private static UITweenRunner instance;

            public static UITweenRunner Instance
            {
                get
                {
                    if (instance == null)
                    {
                        var go = new GameObject("[UITweenRunner]");
                        instance = go.AddComponent<UITweenRunner>();
                        UnityEngine.Object.DontDestroyOnLoad(go);
                    }
                    return instance;
                }
            }
        }
        
        #endregion

        #region Helper Methods

        private static void RegisterTween(object target, Coroutine coroutine)
        {
            if (activeTweens.TryGetValue(target, out Coroutine existing))
            {
                if (existing != null && UITweenRunner.Instance != null)
                {
                    UITweenRunner.Instance.StopCoroutine(existing);
                }
            }
            activeTweens[target] = coroutine;
        }

        private static void UnregisterTween(object target)
        {
            if (target != null)
            {
                activeTweens.Remove(target);
            }
        }

        #endregion

        #region API

        public static Coroutine BounceIn(Transform target, float duration = 0.3f, Action onComplete = null)
        {
            if (target == null) return null;
            var coroutine = UITweenRunner.Instance.StartCoroutine(BounceInRoutine(target, duration, onComplete));
            RegisterTween(target, coroutine);
            return coroutine;
        }

        private static IEnumerator BounceInRoutine(Transform target, float duration, Action onComplete)
        {
            float elapsed = 0f;
            Vector3 startScale = Vector3.zero;
            Vector3 endScale = Vector3.one;

            if (target != null)
            {
                target.localScale = startScale;
            }

            while (elapsed < duration)
            {
                if (target == null) { UnregisterTween(target); yield break; }
                
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                
                // Custom elastic curve for 0 -> 1.15 -> 0.95 -> 1.0
                float easedT = EaseElasticOut(t);
                
                target.localScale = Vector3.LerpUnclamped(startScale, endScale, easedT);
                yield return null;
            }

            if (target != null)
            {
                target.localScale = endScale;
                UnregisterTween(target);
            }
            
            onComplete?.Invoke();
        }

        public static Coroutine BounceOut(Transform target, float duration = 0.2f, bool destroyOnComplete = false, Action onComplete = null)
        {
            if (target == null) return null;
            var coroutine = UITweenRunner.Instance.StartCoroutine(BounceOutRoutine(target, duration, destroyOnComplete, onComplete));
            RegisterTween(target, coroutine);
            return coroutine;
        }

        private static IEnumerator BounceOutRoutine(Transform target, float duration, bool destroyOnComplete, Action onComplete)
        {
            float elapsed = 0f;
            Vector3 startScale = target.localScale;
            CanvasGroup canvasGroup = target.GetComponent<CanvasGroup>();
            float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;

            while (elapsed < duration)
            {
                if (target == null) { UnregisterTween(target); yield break; }
                
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                
                // Scale from 1.0 -> 1.08 -> 0
                float scaleMultiplier;
                if (t < 0.3f)
                {
                    // Overshoot to 1.08
                    scaleMultiplier = Mathf.Lerp(1f, 1.08f, t / 0.3f);
                }
                else
                {
                    // Drop to 0
                    float dropT = (t - 0.3f) / 0.7f;
                    scaleMultiplier = Mathf.Lerp(1.08f, 0f, dropT * dropT * dropT); 
                }

                target.localScale = startScale * scaleMultiplier;

                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
                }

                yield return null;
            }

            if (target != null)
            {
                target.localScale = Vector3.zero;
                if (canvasGroup != null) canvasGroup.alpha = 0f;
                
                UnregisterTween(target);
                
                if (destroyOnComplete)
                {
                    UnityEngine.Object.Destroy(target.gameObject);
                }
            }
            
            onComplete?.Invoke();
        }

        public static Coroutine SlamIn(Transform target, float duration = 0.4f, Action onComplete = null)
        {
            if (target == null) return null;
            var coroutine = UITweenRunner.Instance.StartCoroutine(SlamInRoutine(target, duration, onComplete));
            RegisterTween(target, coroutine);
            return coroutine;
        }

        private static IEnumerator SlamInRoutine(Transform target, float duration, Action onComplete)
        {
            float elapsed = 0f;
            Vector3 endScale = Vector3.one;

            if (target != null)
            {
                target.localScale = endScale * 2.5f;
            }

            while (elapsed < duration)
            {
                if (target == null) { UnregisterTween(target); yield break; }
                
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                
                // 2.5 -> 0.9 -> 1.05 -> 1.0
                float scale = 1f;
                if (t < 0.5f)
                {
                    float normT = t / 0.5f;
                    scale = Mathf.Lerp(2.5f, 0.9f, 1f - Mathf.Pow(1f - normT, 3f)); 
                }
                else if (t < 0.75f)
                {
                    float normT = (t - 0.5f) / 0.25f;
                    scale = Mathf.Lerp(0.9f, 1.05f, normT);
                }
                else
                {
                    float normT = (t - 0.75f) / 0.25f;
                    scale = Mathf.Lerp(1.05f, 1.0f, normT);
                }

                target.localScale = endScale * scale;
                yield return null;
            }

            if (target != null)
            {
                target.localScale = endScale;
                UnregisterTween(target);
            }
            
            onComplete?.Invoke();
        }

        public static Coroutine PunchScale(Transform target, float intensity = 1.2f, float duration = 0.15f, Action onComplete = null)
        {
            if (target == null) return null;
            var coroutine = UITweenRunner.Instance.StartCoroutine(PunchScaleRoutine(target, intensity, duration, onComplete));
            RegisterTween(target, coroutine);
            return coroutine;
        }

        private static IEnumerator PunchScaleRoutine(Transform target, float intensity, float duration, Action onComplete)
        {
            float elapsed = 0f;
            Vector3 originalScale = target.localScale;

            while (elapsed < duration)
            {
                if (target == null) { UnregisterTween(target); yield break; }
                
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                
                float scaleMult = t < 0.5f 
                    ? Mathf.Lerp(1f, intensity, t / 0.5f) 
                    : Mathf.Lerp(intensity, 1f, (t - 0.5f) / 0.5f);
                    
                target.localScale = originalScale * scaleMult;
                
                yield return null;
            }

            if (target != null)
            {
                target.localScale = originalScale;
                UnregisterTween(target);
            }
            
            onComplete?.Invoke();
        }

        public static Coroutine ElasticSlide(RectTransform target, Vector2 from, Vector2 to, float duration = 0.35f, Action onComplete = null)
        {
            if (target == null) return null;
            var coroutine = UITweenRunner.Instance.StartCoroutine(ElasticSlideRoutine(target, from, to, duration, onComplete));
            RegisterTween(target, coroutine);
            return coroutine;
        }

        private static IEnumerator ElasticSlideRoutine(RectTransform target, Vector2 from, Vector2 to, float duration, Action onComplete)
        {
            float elapsed = 0f;

            if (target != null)
            {
                target.anchoredPosition = from;
            }

            while (elapsed < duration)
            {
                if (target == null) { UnregisterTween(target); yield break; }
                
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedT = EaseElasticOut(t);
                
                target.anchoredPosition = Vector2.LerpUnclamped(from, to, easedT);
                yield return null;
            }

            if (target != null)
            {
                target.anchoredPosition = to;
                UnregisterTween(target);
            }
            
            onComplete?.Invoke();
        }

        public static Coroutine FadeCanvasGroup(CanvasGroup group, float from, float to, float duration = 0.25f, Action onComplete = null)
        {
            if (group == null) return null;
            var coroutine = UITweenRunner.Instance.StartCoroutine(FadeCanvasGroupRoutine(group, from, to, duration, onComplete));
            RegisterTween(group, coroutine);
            return coroutine;
        }

        private static IEnumerator FadeCanvasGroupRoutine(CanvasGroup group, float from, float to, float duration, Action onComplete)
        {
            float elapsed = 0f;

            if (group != null)
            {
                group.alpha = from;
            }

            while (elapsed < duration)
            {
                if (group == null) { UnregisterTween(group); yield break; }
                
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                
                group.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }

            if (group != null)
            {
                group.alpha = to;
                UnregisterTween(group);
            }
            
            onComplete?.Invoke();
        }

        public static Coroutine CountTo(TMP_Text text, int from, int to, float duration = 0.4f, Action onComplete = null)
        {
            if (text == null) return null;
            var coroutine = UITweenRunner.Instance.StartCoroutine(CountToRoutine(text, from, to, duration, onComplete));
            RegisterTween(text, coroutine);
            return coroutine;
        }

        private static IEnumerator CountToRoutine(TMP_Text text, int from, int to, float duration, Action onComplete)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (text == null) { UnregisterTween(text); yield break; }
                
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                
                int currentValue = Mathf.RoundToInt(Mathf.Lerp(from, to, t));
                text.text = currentValue.ToString();
                
                yield return null;
            }

            if (text != null)
            {
                text.text = to.ToString();
                UnregisterTween(text);
            }
            
            onComplete?.Invoke();
        }

        public static Coroutine ShakeTransform(Transform target, float intensity = 5f, float duration = 0.2f, Action onComplete = null)
        {
            if (target == null) return null;
            var coroutine = UITweenRunner.Instance.StartCoroutine(ShakeTransformRoutine(target, intensity, duration, onComplete));
            RegisterTween(target, coroutine);
            return coroutine;
        }

        private static IEnumerator ShakeTransformRoutine(Transform target, float intensity, float duration, Action onComplete)
        {
            float elapsed = 0f;
            Vector3 originalPos = target.localPosition;

            while (elapsed < duration)
            {
                if (target == null) { UnregisterTween(target); yield break; }
                
                elapsed += Time.unscaledDeltaTime;
                
                Vector3 randomOffset = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f),
                    0f
                ) * intensity;
                
                float taper = 1f - (elapsed / duration);
                target.localPosition = originalPos + (randomOffset * taper);
                
                yield return null;
            }

            if (target != null)
            {
                target.localPosition = originalPos;
                UnregisterTween(target);
            }
            
            onComplete?.Invoke();
        }

        public static Coroutine StaggeredBounceChildren(Transform parent, float delayBetween = 0.04f, Action onComplete = null)
        {
            if (parent == null) return null;
            var coroutine = UITweenRunner.Instance.StartCoroutine(StaggeredBounceChildrenRoutine(parent, delayBetween, onComplete));
            RegisterTween(parent, coroutine);
            return coroutine;
        }

        private static IEnumerator StaggeredBounceChildrenRoutine(Transform parent, float delayBetween, Action onComplete)
        {
            List<Transform> children = new List<Transform>();
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                child.localScale = Vector3.zero; 
                children.Add(child);
            }

            int completedCount = 0;
            Action onChildComplete = () => { completedCount++; };

            foreach (var child in children)
            {
                if (child == null)
                {
                    completedCount++;
                    continue;
                }
                
                BounceIn(child, 0.3f, onChildComplete);
                
                float timer = 0f;
                while (timer < delayBetween)
                {
                    timer += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            while (completedCount < children.Count)
            {
                yield return null;
            }
            
            UnregisterTween(parent);
            onComplete?.Invoke();
        }

        #endregion
    }

