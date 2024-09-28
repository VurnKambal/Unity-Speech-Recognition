using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;


public class SpeechRecognitionEvents: MonoBehaviour
{

    private UIDocument _document;

    private Button _button;


    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _button = _document.rootVisualElement.Q<Button>("StartButton");
        _button.RegisterCallback<ClickEvent>(OnStartClick);
    }



    private void OnDisable()
    {
        _button.UnregisterCallback<ClickEvent>(OnStartClick);
    }


    private void OnStartClick(ClickEvent evt)
    {
        Debug.Log("Start Recording");
    }



        // Start is called before the first frame update
        void Start()
    {
        

    }

       
    // Update is called once per frame
    void Update()
    {
        
    }
}
