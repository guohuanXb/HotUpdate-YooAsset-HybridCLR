using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace Runtime
{
    public static class YooAssetManager
    {
        private const string DefaultPackageName = "DefaultPackage";

        /// <summary>
        /// 初始化YooAsset资源管理系统，并设置默认的资源包。
        /// </summary>
        public static async UniTask<ResourcePackage> InitializePackage(EPlayMode playMode, AssetServerConfig config)
        {
            // 初始化资源系统
            YooAssets.Initialize();
            // 获取指定的资源包，如果没有找到不会报错
            var package = YooAssets.TryGetPackage(DefaultPackageName) ?? YooAssets.CreatePackage(DefaultPackageName);
            // 设置该资源包为默认的资源包，可以使用YooAssets相关加载接口加载该资源包内容。
            YooAssets.SetDefaultPackage(package);
            if (await InitPackageAsync(playMode, package,config))
            {
                return package;
            }

            return null;
        }
        
        /// <summary>
        /// 初始化资源包（根据运行模式选择不同策略）。
        /// </summary>
        static async UniTask<bool> InitPackageAsync(EPlayMode playMode,ResourcePackage package,AssetServerConfig config)
        {
            InitializationOperation initOperation = null;

            switch (playMode)
            {
                case EPlayMode.EditorSimulateMode:
                {
                    var buildResult = EditorSimulateModeHelper.SimulateBuild(DefaultPackageName);
                    var packageRoot = buildResult.PackageRootDirectory;
                    var fileSystemParams =
                        FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
                    var createParameters = new EditorSimulateModeParameters
                    {
                        EditorFileSystemParameters = fileSystemParams
                    };
                    initOperation = package.InitializeAsync(createParameters);
                    break;
                }
                case EPlayMode.OfflinePlayMode:
                {
                    var fileSystemParams =
                        FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                    var createParameters = new OfflinePlayModeParameters
                    {
                        BuildinFileSystemParameters = fileSystemParams
                    };
                    initOperation = package.InitializeAsync(createParameters);
                    break;
                }
                case EPlayMode.HostPlayMode:
                {
                    // TODO: 正式上线时替换为真实CDN地址
                    string defaultHostServer = config.defaultHostServer;
                    string fallbackHostServer = config.defaultHostServer;
                    IRemoteServices remoteServices =
                        new RemoteServices(defaultHostServer, fallbackHostServer);
                    var cacheFileSystemParams =
                        FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);
                    var buildinFileSystemParams =
                        FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                    var createParameters = new HostPlayModeParameters
                    {
                        BuildinFileSystemParameters = buildinFileSystemParams,
                        CacheFileSystemParameters = cacheFileSystemParams
                    };
                    initOperation = package.InitializeAsync(createParameters);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(playMode), playMode, "不支持的运行模式");
            }

            await initOperation.ToUniTask();

            if (initOperation.Status == EOperationStatus.Succeed)
            {
                Debug.Log("资源包初始化成功！");
                return true;
            }
            else
            {
                Debug.LogError($"资源包初始化失败：{initOperation.Error}");
                return false;
            }
        }
        /// <summary>
        /// 请求获取最新的资源包版本号
        /// </summary>
        /// <param name="package">目标包裹</param>
        /// <returns>最新版本号，失败时返回 null</returns>
        public static async UniTask<string> RequestPackageVersion(ResourcePackage package)
        {
            var operation = package.RequestPackageVersionAsync();
            await operation.ToUniTask();

            if (operation.Status == EOperationStatus.Succeed)
            {
                //更新成功
                string packageVersion = operation.PackageVersion;
                Debug.Log($"请求包裹成功 , 版本 : {packageVersion}");
                return packageVersion;
            }
            else
            {
                //更新失败
                Debug.LogError($"请求包裹失败！ 失败信息 : {operation.Error}");
                return null;
            }
        }
        
        
        /// <summary>
        ///  更新资源包清单（Manifest）
        /// </summary>
        /// <param name="package">目标包裹</param>
        /// <param name="packageVersion">目标版本号</param>
        public static async UniTask<bool> UpdatePackageManifest(ResourcePackage package,string packageVersion)
        {
            var operation = package.UpdatePackageManifestAsync(packageVersion);
            await operation.ToUniTask();

            if (operation.Status == EOperationStatus.Succeed)
            {
                //更新成功
                Debug.Log($"更新包裹清单成功 , 版本 : {packageVersion}");
                return true;
            }
            else
            {
                //更新失败
                Debug.LogError($"更新包裹清单失败！ 失败信息 : {operation.Error}");
                return false;
            }
        }
        /// <summary>
        /// 获取需要下载的资源数
        /// </summary>
        /// <param name="package">目标包裹</param>
        /// <returns>补丁下载器</returns>
        public static ResourceDownloaderOperation GetDownloader(ResourcePackage package)
        {
            int downloadingMaxNum = 10;
            int failedTryAgain = 3;
            var downloader = package.CreateResourceDownloader(downloadingMaxNum, failedTryAgain);
            return downloader;
        }
        /// <summary>
        /// 资源包下载
        /// </summary>
        /// <param name="package">目标包裹</param>
        /// <param name="onDownloadFileBeginCallback">当开始下载某个文件回调</param>
        /// <param name="onDownloadUpdateCallback">当下载进度发生变化回调</param>
        /// <param name="onDownloadFinishCallback">当下载器结束（无论成功或失败）回调</param>
        /// <param name="onDownloadErrorCallback">当下载器发生错误回调</param>
        /// <returns></returns>
        public static async UniTask<bool> Download(ResourcePackage package,
            Action<string,long> onDownloadFileBeginCallback = null,
            Action<long,long,float> onDownloadUpdateCallback = null,
            Action<bool> onDownloadFinishCallback = null,
            Action<string,string> onDownloadErrorCallback = null
            )
        {
            var downloader = GetDownloader(package);
            if (downloader.TotalDownloadCount == 0)
            {
                Debug.Log("没有需要更新的资源！");
                return true;
            }

            Debug.Log($"需要下载{downloader.TotalDownloadCount}个文件 , 共{downloader.TotalDownloadBytes/1024f/1024f:F2} MB");

            #region 注册回调方法
            downloader.DownloadFinishCallback = data =>
            {
                onDownloadFinishCallback?.Invoke(data.Succeed);
            }; //当下载器结束（无论成功或失败）
            downloader.DownloadErrorCallback = data =>
            {
                onDownloadErrorCallback?.Invoke(data.FileName,data.ErrorInfo);
            }; //当下载器发生错误
            downloader.DownloadUpdateCallback = data =>
            {
                onDownloadUpdateCallback?.Invoke(data.CurrentDownloadBytes,data.TotalDownloadBytes,data.Progress);
            }; //当下载进度发生变化
            downloader.DownloadFileBeginCallback = data =>
            {
                onDownloadFileBeginCallback?.Invoke(data.FileName,data.FileSize);
            }; //当开始下载某个文件
            

            #endregion
            
            downloader.BeginDownload();
            await downloader.ToUniTask();
            
            if (downloader.Status == EOperationStatus.Succeed)
            {
                Debug.Log("下载文件包裹成功！");
                return true;
            }
            else
            {
                Debug.LogError($"下载文件包裹失败: {downloader.Error}");
                return false;
            }
        }

        
        
        /// <summary>
        /// 销毁默认资源包，并从YooAssets系统中移除。
        /// 该方法首先尝试销毁名为"DefaultPackage"的资源包。如果销毁成功，则会从YooAssets系统中移除该资源包。
        /// 整个过程是异步的，使用UniTask来等待销毁操作完成。
        /// </summary>
        /// <returns>返回一个表示销毁和移除操作完成的UniTask。</returns>
        public static async UniTask DestroyPackage()
        {
            // 先销毁资源包
            var package = YooAssets.GetPackage(DefaultPackageName);
            DestroyOperation operation = package.DestroyAsync();
            await operation.ToUniTask();
            if (operation.Status == EOperationStatus.Succeed)
            {
                Debug.Log("DestroyPackage Success");
                //然后移除资源包
                if (YooAssets.RemovePackage(package))
                {
                    Debug.Log("RemovePackage Success");
                }
                else
                {
                    Debug.LogError("DestroyPackage Fail");
                }
            }
            else
            {
                Debug.LogError("RemovePackage Fail");
            }
        }

        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <param name="location"></param>
        /// <param name="package"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static async UniTask<T> LoadAssetAsync<T>(string location,ResourcePackage package) where T : UnityEngine.Object
        {
            AssetHandle handle = package.LoadAssetAsync<T>(location);
            await handle.ToUniTask();
            if (handle.Status == EOperationStatus.Succeed)
            {
                Debug.Log($"加载资源{location}成功!");
                return handle.AssetObject as T;
            }
            Debug.LogError($"加载资源{location}失败!");
            return null;
        }
    }
    
    /// <summary>
    /// 远端资源地址查询服务类
    /// </summary>
    internal class RemoteServices : IRemoteServices
    {
        private readonly string _defaultHostServer;
        private readonly string _fallbackHostServer;

        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostServer = defaultHostServer;
            _fallbackHostServer = fallbackHostServer;
        }
        string IRemoteServices.GetRemoteMainURL(string fileName)
        {
            return $"{_defaultHostServer}/{fileName}";
        }
        string IRemoteServices.GetRemoteFallbackURL(string fileName)
        {
            return $"{_fallbackHostServer}/{fileName}";
        }
    }
}