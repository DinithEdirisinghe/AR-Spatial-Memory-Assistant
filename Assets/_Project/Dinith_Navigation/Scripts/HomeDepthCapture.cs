using System;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialSense.RoomScan
{
    public sealed class HomeDepthFrame
    {
        public DepthPatchSample[] Samples;
        public MeshPoint Origin;
    }
    // Dense asynchronous acquisition, with the existing MRUK ray sampler kept
    // as fallback by HomeScanner. One bounded GPU request can be in flight.
    public sealed class HomeDepthCapture : IDisposable
    {
        public const int Size=48;
        private EnvironmentDepthManager manager;
        private bool ownsManager,pending,failed;
        private ComputeShader shader;
        private ComputeBuffer buffer;
        private AsyncGPUReadbackRequest request;
        private int requestGeneration;
        private float requestedAt;
        private MeshPoint requestedOrigin;
        public bool Available => !failed && manager!=null && manager.IsDepthAvailable && shader!=null;
        public string Mode => Available?"Dense depth":"MRUK depth fallback";
        public void Enable(Transform trackingSpace)
        {
            try
            {
                // Visual occlusion needs live depth even if dense mesh readback
                // has fallen back to the public MRUK ray sampler.
                if(!EnvironmentDepthManager.IsSupported){failed=true;return;}
                if(manager==null)
                {
                    manager=UnityEngine.Object.FindAnyObjectByType<EnvironmentDepthManager>();
                    if(manager==null){manager=new GameObject("Home continuous depth").AddComponent<EnvironmentDepthManager>();ownsManager=true;manager.RemoveHands=true;}
                }
                manager.CustomTrackingSpace=trackingSpace;manager.enabled=true;
                manager.OcclusionShadersMode=OcclusionShadersMode.SoftOcclusion;
                if(failed)return;
                if(!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback){failed=true;return;}
                if(shader==null)
                {
                    var asset=Resources.Load<ComputeShader>("HomeDepthReadback");
                    if(asset==null){failed=true;return;}shader=UnityEngine.Object.Instantiate(asset);
                    buffer=new ComputeBuffer(Size*Size,16);
                }
            }
            catch(Exception e){failed=true;Debug.LogWarning("[Home depth] Dense capture unavailable; using ray samples. "+e.Message);}
        }
        public HomeDepthFrame Poll(int generation)
        {
            if(!pending || !request.done)return null;
            pending=false;
            if(requestGeneration!=generation || Time.unscaledTime-requestedAt>1)return null;
            if(request.hasError){failed=true;return null;}
            var data=request.GetData<Vector4>();var points=new DepthPatchSample[Size*Size];
            for(var i=0;i<points.Length;i++){var p=data[i];points[i]=new DepthPatchSample(p.w>.5f,new MeshPoint(p.x,p.y,p.z));}
            return new HomeDepthFrame {Samples=points,Origin=requestedOrigin};
        }
        public void Request(Transform frame,Vector3 head,int generation)
        {
            if(pending || !Available)return;
            var texture=Shader.GetGlobalTexture("_EnvironmentDepthTexture");
            var matrices=Shader.GetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices");
            if(texture==null || texture.dimension!=TextureDimension.Tex2DArray || matrices==null || matrices.Length<1 || Math.Abs(matrices[0].determinant)<.000001)return;
            try
            {
                shader.SetTexture(0,"_HomeDepth",texture);shader.SetBuffer(0,"_HomePoints",buffer);
                shader.SetInt("_HomeTextureSize",texture.width);
                shader.SetMatrix("_HomeClipToLocal",frame.worldToLocalMatrix*matrices[0].inverse);
                var local=frame.InverseTransformPoint(head);requestedOrigin=new MeshPoint(local.x,local.y,local.z);
                shader.SetVector("_HomeHeadLocal",local);
                shader.Dispatch(0,Size/8,Size/8,1);request=AsyncGPUReadback.Request(buffer);pending=true;
                requestGeneration=generation;requestedAt=Time.unscaledTime;
            }
            catch(Exception e){failed=true;Debug.LogWarning("[Home depth] Switching to ray samples: "+e.Message);}
        }
        public void Disable(){if(ownsManager && manager!=null)manager.enabled=false;}
        public void Cancel(){requestGeneration=-1;}
        public void Reject(){failed=true;Debug.LogWarning("[Home depth] Depth/raycast alignment disagreed; keeping the MRUK ray fallback.");}
        public void Dispose()
        {
            // This occurs only on teardown; never block the render loop while scanning.
            if(pending)request.WaitForCompletion();buffer?.Release();
            if(shader!=null)UnityEngine.Object.Destroy(shader);
            if(ownsManager && manager!=null)UnityEngine.Object.Destroy(manager.gameObject);
        }
    }
}
