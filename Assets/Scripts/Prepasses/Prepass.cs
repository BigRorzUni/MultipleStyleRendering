using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public abstract class Prepass : ScriptableRenderPass, IDisposable
{
    public readonly string PassName;

    protected Prepass(string passName)
    {
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        profilingSampler = new ProfilingSampler(passName);
        PassName = passName;
    }

    public virtual void Dispose() { }
}