﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;

public class TimeHandler : MonoBehaviour
{
    public VideoPlayer vp;
    public Text timeCurr;
    public Text timeTotal;
    public Slider timeSlider;

    private void Awake()
    {
        // Set the window to fill the screen without black borders
       // Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow, 60);

        Physics.autoSimulation = false;
        Physics2D.autoSimulation = false;
    }

    void Start()
    {
        StartCoroutine(UpdateTime());
        vp.loopPointReached += CheckOver;
        vp.errorReceived += ErrorReceived;
    }
    
    IEnumerator UpdateTime()
    {
        while (true)
        {
            if (!vp.isPaused)
            {
                // Adjust time displays
                timeCurr.text = (System.Math.Floor(vp.time/60)).ToString()+':'+System.Math.Floor(vp.time%60).ToString("00");

                // Adjust slider position
                //Debug.Log("Length = " + vp.length);
                //Debug.Log("Slider pos = " + (float)(vp.time / vp.length));
                timeSlider.normalizedValue = (float)(vp.time / vp.length);
                //timeSlider.SetValueWithoutNotify((float)(vp.time / vp.length));
                //Debug.Log("Slider value = " + timeSlider.normalizedValue);

                //TODO: ENSURE VISUAL UPDATE OF TIME SLIDER EVERY HALF-SECOND.
            }
            yield return new WaitForSeconds(0.5f);
        }
    }

    void CheckOver(VideoPlayer vp)
    {
        ForceUpdateTimeAndSlider();
    }

    void ErrorReceived(object sender, string message)
    {
        GetComponent<InputHandler>().HandleVideoError(sender, message);
    }

    public void ForceUpdateTime()
    {
        // Adjust time displays
        timeCurr.text = (System.Math.Floor(vp.time / 60)).ToString() + ':' + System.Math.Floor(vp.time % 60).ToString("00");
        timeTotal.text = (System.Math.Floor(vp.length / 60)).ToString() + ':' + System.Math.Floor(vp.length % 60).ToString("00");
    }

    public void ForceUpdateSlider()
    {
        // Adjust slider position
        //Debug.Log("Length = " + vp.length);
        //Debug.Log("Slider pos = " + (float)(vp.time / vp.length));
        timeSlider.normalizedValue = (float)(vp.time / vp.length);
        //Debug.Log("Slider value = " + timeSlider.normalizedValue);
    }

    public void ForceUpdateTimeAndSlider(double time)
    {
        ForceUpdateTime(time);
        ForceUpdateSlider(time);
    }

    public void ForceUpdateTime(double time)
    {
        // Adjust time displays
        timeCurr.text = (System.Math.Floor(time / 60)).ToString() + ':' + System.Math.Floor(time % 60).ToString("00");
    }

    public void ForceUpdateSlider(double time)
    {
        // Adjust slider position
        //Debug.Log("Length = " + vp.length);
        //Debug.Log("Slider pos = " + (float)(vp.time / vp.length));
        timeSlider.normalizedValue = (float)(time / vp.length);
       // Debug.Log("Slider value = " + timeSlider.normalizedValue);
    }

    public void ForceUpdateTimeAndSlider()
    {
        ForceUpdateTime();
        ForceUpdateSlider();
    }

    public void UpdateDraggedTime()
    {
       // Debug.Log("Length = " + vp.length);
       // Debug.Log("Slider pos = " + (float)(vp.time / vp.length));
        double time = (timeSlider.normalizedValue) * vp.length;
        timeCurr.text = (System.Math.Floor(time / 60)).ToString() + ':' + System.Math.Floor(time % 60).ToString("00");
       // Debug.Log("Slider value = " + timeSlider.normalizedValue);
		
		
    }

    public void StartTimer()
    {
        StartCoroutine(UpdateTime());
    }

    public void UpdateTotalTime()
    {
        StartCoroutine(PopulateTotalTime());
    }

    /// <summary>
    /// Waits for half a second, then updates the total duration of the video ONCE.
    /// </summary>
    /// <returns></returns>
    public IEnumerator PopulateTotalTime()
    {
        yield return new WaitForSeconds(0.5f);
        timeTotal.text = (System.Math.Floor(vp.length / 60)).ToString() + ':' + System.Math.Floor(vp.length % 60).ToString("00");
    }
}
