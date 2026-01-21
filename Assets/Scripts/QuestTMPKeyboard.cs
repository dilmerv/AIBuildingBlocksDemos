using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TMP_InputField))]
public class QuestTMPKeyboard : MonoBehaviour, IPointerDownHandler
{
    private TMP_InputField inputField;
    private TouchScreenKeyboard keyboard;
    private string lastText;

    private bool keyboardRequested;
    private Coroutine openRoutine;
    
    public string KeyboardText => inputField.text;
    
    void Awake()
    {
        inputField = GetComponent<TMP_InputField>();

        inputField.onSelect.AddListener(_ => RequestKeyboard());
        inputField.onDeselect.AddListener(_ => CloseKeyboard());
    }

    void Update()
    {
        if (keyboard == null)
            return;

        if (keyboard.text != lastText)
        {
            lastText = keyboard.text;
            inputField.text = lastText;
            inputField.caretPosition = lastText.Length;
        }

        if (keyboard.status == TouchScreenKeyboard.Status.Done ||
            keyboard.status == TouchScreenKeyboard.Status.Canceled)
        {
            CloseKeyboard();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        RequestKeyboard();
    }

    private void RequestKeyboard()
    {
        if (keyboard != null)
            return;

        if (openRoutine != null)
            StopCoroutine(openRoutine);

        openRoutine = StartCoroutine(OpenKeyboardWithDelay(0.05f));
    }

    private IEnumerator OpenKeyboardWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay); // avoid Quest race condition

        if (!inputField.isFocused)
            yield break;

        keyboard = TouchScreenKeyboard.Open(
            inputField.text,
            TouchScreenKeyboardType.Default
        );

        lastText = inputField.text;
    }

    private void CloseKeyboard()
    {
        if (keyboard != null)
            keyboard.active = false;

        keyboard = null;
    }
}