using UnityEngine;

namespace Runtime
{
    /// <summary>
    /// 服务器设置 ScriptableObject
    /// 用于在编辑器中配置服务器地址等信息
    /// </summary>
    [CreateAssetMenu(fileName = "ServerSetting", menuName = "Game/Server Setting")]
    public class ServerSetting : ScriptableObject
    {
        [Header("服务器配置")]
        [Tooltip("游戏服务器地址")]
        public string serverUrl = "http://localhost:8080";

        [Tooltip("资源服务器地址")]
        public string resourceUrl = "http://localhost:8080/resources";

        [Tooltip("版本信息接口地址")]
        public string versionUrl = "http://localhost:8080/version";

        [Header("HTTP 请求配置")]
        [Tooltip("配置 JSON 的 URL")]
        public string configUrl = "http://localhost:8080/config.json";

        [Tooltip("请求超时时间（秒）")]
        [Range(5f, 60f)]
        public float timeoutSeconds = 10f;

        [Header("缓存配置")]
        [Tooltip("是否启用本地缓存")]
        public bool enableCache = true;

        [Tooltip("缓存过期时间（小时），0 表示永不过期")]
        [Range(0f, 720f)]
        public float cacheExpirationHours = 24f;

        /// <summary>
        /// 获取默认的服务器配置
        /// </summary>
        public ServerConfig GetDefaultConfig()
        {
            return new ServerConfig
            {
                ServerUrl = serverUrl,
                ResourceUrl = resourceUrl,
                VersionUrl = versionUrl
            };
        }
    }
}
