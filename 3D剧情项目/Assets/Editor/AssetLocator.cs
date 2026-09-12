// 在 Assets 里按名字找文件夹/文件（返回 “Assets/...” 形式路径），目录整理后路径依然有效。
using System.IO;
using System.Linq;

public static class AssetLocator
{
    public static string Dir(string folderName)
    {
        if (!Directory.Exists("Assets")) return null;
        var hit = Directory.GetDirectories("Assets", folderName, SearchOption.AllDirectories).FirstOrDefault();
        return hit == null ? null : hit.Replace('\\', '/');
    }

    public static string FileNamed(string fileName)
    {
        if (!Directory.Exists("Assets")) return null;
        var hit = Directory.GetFiles("Assets", fileName, SearchOption.AllDirectories).FirstOrDefault();
        return hit == null ? null : hit.Replace('\\', '/');
    }
}

// touch 213115
