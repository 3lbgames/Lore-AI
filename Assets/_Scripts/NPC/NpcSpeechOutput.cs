using System;
using UnityEngine;

public class NpcSpeechOutput : MonoBehaviour
{
    public bool wrapText = true;
    public int wrapAt = 5;

    public string Text { get; private set; }
    public event Action Changed;

    public void SetText(string text)
    {
        if (wrapText) Text = TextWrapUtility.WrapEveryNWords(text, wrapAt);
        else Text = text;
        Changed?.Invoke();
    }

    public void Clear() => SetText(null);
    private void OnDisable() => Clear();
}
