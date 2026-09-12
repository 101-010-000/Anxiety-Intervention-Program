// 通用二次确认弹窗（退出游戏 / 重新开始 / 删除存档）
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(UIPanel))]
public class ConfirmDialog : MonoBehaviour
{
    public UIPanel panel;
    public Text    titleText;
    public Text    bodyText;
    public Text    okLabel;
    public Text    cancelLabel;
    public Button  okButton;
    public Button  cancelButton;

    System.Action _onOk;
    System.Action _onCancel;

    void Awake()
    {
        if (panel == null) panel = GetComponent<UIPanel>();
        if (okButton != null)     okButton.onClick.AddListener(OnOk);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancel);
    }

    public void Open(string title, string body, System.Action onOk,
                     string ok = "确定", string cancel = "取消",
                     System.Action onCancel = null, bool showCancel = true)
    {
        if (titleText != null)  titleText.text = title;
        if (bodyText != null)   bodyText.text = body;
        if (okLabel != null)    okLabel.text = ok;
        if (cancelLabel != null) cancelLabel.text = cancel;
        if (cancelButton != null) cancelButton.gameObject.SetActive(showCancel);
        _onOk = onOk;
        _onCancel = onCancel;
        if (panel == null) panel = GetComponent<UIPanel>();
        if (panel != null) panel.Show();
    }

    void OnOk()
    {
        if (panel != null) panel.Hide();
        var cb = _onOk; _onOk = null; _onCancel = null;
        if (cb != null) cb();
    }

    void OnCancel()
    {
        if (panel != null) panel.Hide();
        var cb = _onCancel; _onOk = null; _onCancel = null;
        if (cb != null) cb();
    }

    public void Close()
    {
        if (panel != null) panel.Hide();
        _onOk = null; _onCancel = null;
    }
}
