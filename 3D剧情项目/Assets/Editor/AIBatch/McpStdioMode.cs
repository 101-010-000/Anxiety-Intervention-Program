using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AIBatch
{
    /// <summary>
    /// unity-mcp (CoplayDev) 的两个冷启动问题：
    /// 1. 默认 UseHttpTransport=true（HTTP 模式），而 AI 客户端（ZCode/pi）以 stdio 拉起的
    ///    server 只连 stdio Bridge 的 6400 端口 —— 模式错配导致永远连不上。
    /// 2. StdioBridgeHost 无 [InitializeOnLoad]，静态构造懒加载；ReloadHandler 只管
    ///    "重载后恢复"，编辑器冷启动时没有任何代码触发 Bridge 启动。
    /// 此处强制 stdio 模式并主动启动 Bridge。项目未安装 unity-mcp 包时自动跳过。
    /// </summary>
    static class McpStdioMode
    {
        const string PrefKey = "MCPForUnity.UseHttpTransport";
        const string BridgeTypeName =
            "MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost, MCPForUnity.Editor";

        [InitializeOnLoadMethod]
        static void ForceStdioTransportAndStartBridge()
        {
            if (EditorPrefs.GetBool(PrefKey, true))
            {
                EditorPrefs.SetBool(PrefKey, false);
            }

            EditorApplication.delayCall += TryStartBridge;
        }

        static void TryStartBridge()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryStartBridge;
                return;
            }

            try
            {
                Type t = Type.GetType(BridgeTypeName);
                if (t == null) return; // 未安装 unity-mcp 包

                var isRunning = t.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static);
                if (isRunning != null && (bool)isRunning.GetValue(null)) return;

                t.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                Debug.Log("[AIBatch] unity-mcp stdio Bridge 已启动");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIBatch] 启动 unity-mcp Bridge 失败: {e.Message}");
            }
        }
    }
}
