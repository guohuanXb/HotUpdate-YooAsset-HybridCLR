using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HybridCLR;
using UnityEngine;
using UnityEngine.UI;
using YooAsset;
using PrimeTween;
namespace  Runtime
{
    [Serializable]
    public struct AssetServerConfig
    {
        public string defaultHostServer;
        public string fallbackHostServe;

        public AssetServerConfig(string defaultHostServer ,string fallbackHostServe)
        {
            this.defaultHostServer = defaultHostServer;
            this.fallbackHostServe = fallbackHostServe;
        }
    }

    public class AssetLauncher : MonoBehaviour
    {
        public EPlayMode runningMode = EPlayMode.EditorSimulateMode;

        public AssetServerConfig config =
            new AssetServerConfig("http://172.17.127.72/PC/v1.0", "http://172.17.127.72/PC/v1.0");

        public Slider progressBar;
        public RectTransform processImage;
        public CanvasGroup tips;
        private static Dictionary<string, TextAsset> _assetsDic = new();

        private static List<string> AOTMetaAssemblyFiles { get; } = new()
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
            "UnityEngine.CoreModule.dll",
            "UniTask.dll"
        };

        private static Assembly _hotUpdateAss;

        private void Start()
        {

#if UNITY_EDITOR
            runningMode = EPlayMode.EditorSimulateMode;

#else
            runningMode = EPlayMode.HostPlayMode;
#endif

            Launcher().Forget();
        }

        async UniTaskVoid Launcher()
        {
            // 1. 初始化包裹
            var package = await YooAssetManager.InitializePackage(runningMode, config);
            if (package == null) return;
            // 2. 请求最新版本号
            string packageVersion = await YooAssetManager.RequestPackageVersion(package);
            if (packageVersion == null) return;
            // 3. 更新资源清单
            bool manifestUpdated = await YooAssetManager.UpdatePackageManifest(package, packageVersion);
            if (!manifestUpdated) return;
            // 4. 下载的资源
            bool downloadSuccess = await YooAssetManager.Download(package,
                OnDownloadFileBeginCallback,
                OnDownloadUpdateCallback,
                OnDownloadFinishCallback,
                OnDownloadErrorCallback
            );


            // 5. 加载程序集文件
            await LoadDllFiles(package);

            // 6. 加载AOT程序集元数据
            await LoadMetadataForAOTAssemblyAsync();
            // 7. 加载热更新程序集
            await LoadHotUpdateAssemblyAsync();
            // 8. 启动游戏
            await StartGame(package);
        }

        async UniTask StartGame(ResourcePackage package)
        {
            await Tween.Alpha(tips, endValue: 1f, duration: 0.5f);
            await UniTask.WaitUntil(() => Input.GetMouseButtonDown(0));
            SceneHandle handle = package.LoadSceneAsync("Main");
            await handle.ToUniTask();
            Debug.Log($"{handle.SceneName} 场景加载成功！");
            await Test(package);
        }

        async UniTask Test(ResourcePackage package)
        {
            AssetHandle handle = package.LoadAssetAsync<GameObject>("Cube2");
            await handle.ToUniTask();
            GameObject cube = handle.InstantiateSync();
            cube.transform.position = Vector3.one * 5;
        }

        #region 下载资源回调

        void OnDownloadFileBeginCallback(string fileName, long fileSize)
        {
            Debug.Log($"{fileName} 文件开始下载，大小：{fileSize / 1024f / 1024f} MB ");
        }

        void OnDownloadUpdateCallback(long currentDownloadBytes, long totalDownloadBytes, float progress)
        {
            ProcessPerform(progress);
            Debug.Log($"{currentDownloadBytes / 1024f / 1024f} MB / {totalDownloadBytes / 1024f / 1024f} MB , 进度：{progress}");
        }

        void OnDownloadErrorCallback(string fileName, string error)
        {
            Debug.LogError($"文件 {fileName} ,下载错误：{error}");
        }

        void OnDownloadFinishCallback(bool succeed)
        {
            Debug.Log(succeed ? "文件下载成功！" : "部分文件下载失败！");
        }

        #endregion

        /// <summary>
        /// 加载DLL文件（包括热更程序集和AOT程序集）
        /// </summary>
        /// <param name="package"></param>
        async UniTask LoadDllFiles(ResourcePackage package)
        {
            List<string> assets = new() { "HotUpdate.dll" };
            assets.AddRange(AOTMetaAssemblyFiles);
            foreach (var asset in assets)
            {
                TextAsset assetObj = await YooAssetManager.LoadAssetAsync<TextAsset>(asset, package);
                if (assetObj != null && assetObj.bytes != null && assetObj.bytes.Length > 0)
                {
                    _assetsDic[asset] = assetObj;
                    Debug.Log($"{asset} DLL 加载成功,文件大小：{assetObj.bytes.Length / 1024f} KB");
                }
                else
                {
                    Debug.LogError($"{asset} DLL 加载失败");
                }
            }
        }

        /// <summary>
        /// 从TextAsset文件中读取byte数据
        /// </summary>
        /// <param name="aotDllName"></param>
        /// <returns></returns>
        byte[] ReadBytes(string aotDllName)
        {
            if (_assetsDic.TryGetValue(aotDllName, out TextAsset asset))
            {
                return asset.bytes;
            }

            return Array.Empty<byte>();
        }

        /// <summary>
        /// 为AOT程序集加载元数据
        /// </summary>
        async UniTask LoadMetadataForAOTAssemblyAsync()
        {
            Debug.Log("开始为AOT程序集加载元数据");
            foreach (var aotDllName in AOTMetaAssemblyFiles)
            {
                byte[] dllBytes = ReadBytes(aotDllName);
                var err = HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, HomologousImageMode.SuperSet);
                Debug.Log($"成功加载元数据:{aotDllName}. ret:{err}");
            }

            await UniTask.Yield();
        }

        /// <summary>
        /// 异步加载热更新程序集。
        /// </summary>
        /// <returns>一个表示异步操作的UniTask</returns>
        async UniTask LoadHotUpdateAssemblyAsync()
        {
#if !UNITY_EDITOR
            byte[] dllBytes = ReadBytes("HotUpdate.dll");
            if (dllBytes == null && dllBytes.Length == 0)
            {
                throw new Exception("HotUpdate.dll 加载失败！");
            }

            try
            {
                _hotUpdateAss = Assembly.Load(dllBytes);
                Debug.Log($"成功加载元数据: HotUpdate.dll");
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
#else
            _hotUpdateAss = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "HotUpdate");
            Debug.Log($"成功加载元数据: HotUpdate.dll");
#endif

            await UniTask.Yield();
        }
        /// <summary>
        /// 下载进度表现
        /// </summary>
        /// <param name="process"></param>
        void ProcessPerform(float process)
        {
            progressBar.value = process;
        }
        /// <summary>
        /// 旋转图标
        /// </summary>
        void ProcessPerform()
        {
            Tween.LocalRotation(processImage,endValue: new Vector3(0, 0, -360), 
                duration: 1f, 
                cycles: -1,                    // 无限循环
                cycleMode: CycleMode.Restart,  // 重置后继续
                ease: Ease.Linear);
        }
    }
}

