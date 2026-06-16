using UnityEngine;

/// <summary>
/// Meta SDK (OVRInput) ile sağ controller'ın A/B tuşlarından kalibrasyonu tetikler.
///   A tuşu (sağ) = OVRInput.Button.One  -> A noktası
///   B tuşu (sağ) = OVRInput.Button.Two  -> B noktası
/// OVRInput.GetDown zaten "bu frame'de basıldı" bilgisi verir (edge detection dahili).
/// </summary>
public class CalibrationButtonTriggerOVR : MonoBehaviour
{
    [Header("Referans")]
    public TwoPointCalibrationOVR calibration;

    private void Update()
    {
        // A tuşu -> A noktası
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            calibration.CapturePointA();

        // B tuşu -> B noktası
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            calibration.CapturePointB();
    }
}