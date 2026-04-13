using System;
using System.IO;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Runtime
{
    /// <summary>
    /// 服务器配置数据
    /// </summary>
    [Serializable]
    public class ServerConfig
    {
        [JsonProperty("serverUrl")]
        public string ServerUrl { get; set; } = "http://localhost:8080";

        [JsonProperty("resourceUrl")]
        public string ResourceUrl { get; set; } = "http://localhost:8080/resources";

        [JsonProperty("versionUrl")]
        public string VersionUrl { get; set; } = "http://localhost:8080/version";
    }


    /// <summary>
    /// HTTP 请求器 - 用于获取服务器配置，支持缓存和兜底
    /// </summary>
    public class HttpRequester
    {
        /// <summary>
        /// 缓存文件名
        /// </summary>
        private const string CacheFileName = "server_config.json";

        /// <summary>
        /// StreamingAssets 中的默认配置文件名
        /// </summary>
        private const string DefaultConfigFileName = "server_config.json";

        /// <summary>
        /// 请求超时时间（秒）
        /// </summary>
        private const float TimeoutSeconds = 10f;

        /// <summary>
        /// 获取服务器配置，遵循以下优先级：
        /// 1. 从服务器拉取（成功则缓存）
        /// 2. 使用本地旧缓存
        /// 3. 读取 StreamingAssets 兜底默认值
        /// </summary>
        /// <param name="configUrl">配置 JSON 的 URL</param>
        /// <returns>服务器配置对象</returns>
        public async UniTask<ServerConfig> GetServerConfigAsync(string configUrl)
        {
            ServerConfig config = null;
            // 1. 尝试从服务器拉取 JSON
            Debug.Log("正在尝试从服务器获取配置...");
            try
            {
                config = await FetchFromServerAsync(configUrl);
                if (config != null)
                {
                    // 成功则写入缓存
                    SaveToCache(config);
                    Debug.Log("从服务器获取配置成功，已缓存");
                    return config;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"从服务器获取配置失败：{e.Message}");
            }

            // 2. 尝试从缓存读取
            Debug.Log("尝试从本地缓存读取配置...");
            config = LoadFromCache();
            if (config != null)
            {
                Debug.Log("使用本地缓存配置");
                return config;
            }

            // 3. 使用 StreamingAssets 兜底
            Debug.Log("使用 StreamingAssets 中的默认配置...");
            config = await LoadFromStreamingAssetsAsync();
            if (config != null)
            {
                Debug.Log("使用 StreamingAssets 兜底配置");
                return config;
            }

            // 全部失败，返回默认配置
            Debug.LogWarning("所有配置加载方式均失败，返回空配置");
            return CreateDefaultConfig();
        }

        /// <summary>
        /// 从服务器异步获取配置
        /// </summary>
        private async UniTask<ServerConfig> FetchFromServerAsync(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = (int)TimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await UniTask.Yield();
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    return JsonConvert.DeserializeObject<ServerConfig>(request.downloadHandler.text);
                }
                else
                {
                    throw new Exception($"HTTP 请求失败：{request.error} (状态码：{request.responseCode})");
                }
            }
        }

        /// <summary>
        /// 保存配置到本地缓存 (persistentDataPath)
        /// </summary>
        private void SaveToCache(ServerConfig config)
        {
            try
            {
                string cachePath = Path.Combine(Application.persistentDataPath, CacheFileName);
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                
                File.WriteAllText(cachePath, json);
                Debug.Log($"配置已缓存到：{cachePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"保存缓存失败：{e.Message}");
            }
        }

        /// <summary>
        /// 从本地缓存加载配置
        /// </summary>
        private ServerConfig LoadFromCache()
        {
            try
            {
                string cachePath = Path.Combine(Application.persistentDataPath, CacheFileName);
                if (!File.Exists(cachePath))
                {
                    Debug.Log("缓存文件不存在");
                    return null;
                }

                string json = File.ReadAllText(cachePath);
                return JsonConvert.DeserializeObject<ServerConfig>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"读取缓存失败：{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 StreamingAssets 加载默认配置
        /// </summary>
        private async UniTask<ServerConfig> LoadFromStreamingAssetsAsync()
        {
            try
            {
                string configPath;

                if (Application.platform == RuntimePlatform.Android)
                {
                    configPath = $"jar:file://{Application.dataPath}.apk!/assets/{DefaultConfigFileName}";
                }
                else
                {
                    configPath = Path.Combine(Application.streamingAssetsPath, DefaultConfigFileName);
                }

                using var request = UnityWebRequest.Get(configPath);
                await request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    return JsonConvert.DeserializeObject<ServerConfig>(request.downloadHandler.text);

                Debug.Log($"StreamingAssets 中不存在默认配置文件：{DefaultConfigFileName}");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"从 StreamingAssets 加载配置失败：{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 创建默认配置（兜底）
        /// </summary>
        private ServerConfig CreateDefaultConfig()
        {
            return new ServerConfig
            {
                ServerUrl = "http://localhost:8080",
                ResourceUrl = "http://localhost:8080/resources",
                VersionUrl = "http://localhost:8080/version"
            };
        }

        /// <summary>
        /// 检查缓存是否存在
        /// </summary>
        public bool HasCache()
        {
            string cachePath = Path.Combine(Application.persistentDataPath, CacheFileName);
            return File.Exists(cachePath);
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public void ClearCache()
        {
            try
            {
                string cachePath = Path.Combine(Application.persistentDataPath, CacheFileName);
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                    Debug.Log("缓存已清除");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"清除缓存失败：{e.Message}");
            }
        }
    }
}
