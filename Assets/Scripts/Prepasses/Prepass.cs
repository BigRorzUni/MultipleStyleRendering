using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public abstract class Prepass : ScriptableRenderPass, IDisposable
{
    protected Prepass(string passName)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        profilingSampler = new ProfilingSampler(passName);
    }

    public virtual void Dispose() { }
}