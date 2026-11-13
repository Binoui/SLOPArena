using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace SlopArena.Client.Workshop
{
    public sealed class FighterPresentationReleases : MonoBehaviour
    {
        private static readonly HashSet<IDisposable> Pending = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaySession()
        {
            foreach (var lease in Pending) lease.Dispose();
            Pending.Clear();
        }
        public static void AfterRenderers(IDisposable lease)
        {
            Pending.Add(lease);
            if (!Application.isPlaying) return;
            var host = new GameObject("Fighter presentation retirement");
            DontDestroyOnLoad(host);
            host.AddComponent<FighterPresentationReleases>().StartCoroutine(Release(lease, host));
        }
        private static IEnumerator Release(IDisposable lease, GameObject host)
        {
            yield return null;
            if (Pending.Remove(lease)) lease.Dispose();
            Destroy(host);
        }
    }
}
