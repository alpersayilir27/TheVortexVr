using UnityEngine;

/// <summary>
/// Meta OVRCameraRig için 2 nokta kalibrasyon.
/// Sağ controller'ı yere doğru tutup A/B tuşlarıyla iki nokta alır,
/// fiziksel tracking alanını oyun dünyasına hizalar.
///
/// Her tuşa basışta o noktaya bir silindir işaretçi konur;
/// ikinci nokta alınıp hizalama tamamlanınca iki silindir de silinir.
/// </summary>
public class TwoPointCalibrationOVR : MonoBehaviour
{
    [Header("Meta Rig Referansları")]
    [Tooltip("Sahnedeki OVRCameraRig'in transformu. Tracking space kökü; kaydırılıp döndürülecek olan bu.")]
    public Transform rigRoot;

    [Tooltip("OVRCameraRig > TrackingSpace > RightHandAnchor (sağ controller).")]
    public Transform rightController;

    [Header("Oyun İçi Hedef Noktaları (sahnede sabit)")]
    [Tooltip("Fiziksel A noktasının oyundaki karşılığı")]
    public Transform gamePointA;
    [Tooltip("Fiziksel B noktasının oyundaki karşılığı")]
    public Transform gamePointB;

    [Header("İşaretçi Silindir Ayarları")]
    public Color markerColor = Color.yellow;
    [Tooltip("Silindirin dünya cinsinden yüksekliği (m)")]
    public float markerHeight = 1f;
    [Tooltip("Silindirin çapı (m)")]
    public float markerDiameter = 0.05f;

    private Vector3 _trackedA, _trackedB;
    private bool _hasA, _hasB;
    private GameObject _markerA, _markerB;

    // --- Tuşlara bağlanacak metotlar ---

    public void CapturePointA()
    {
        _trackedA = GetFloorPoint(out Vector3 worldA);
        _hasA = true;
        SpawnMarker(ref _markerA, worldA);
        Debug.Log($"[Calib] A kaydedildi: {_trackedA}");
    }

    public void CapturePointB()
    {
        _trackedB = GetFloorPoint(out Vector3 worldB);
        _hasB = true;
        SpawnMarker(ref _markerB, worldB);
        Debug.Log($"[Calib] B kaydedildi: {_trackedB}");

        if (_hasA && _hasB) Apply();
    }

    public void ResetCalibration()
    {
        _hasA = _hasB = false;
        ClearMarkers();
    }

    // --- Çekirdek ---

    /// <summary>
    /// Sağ controller'ı yere doğru tut; ileri yönündeki ışının zemine (tracking y=0)
    /// değdiği noktayı döndürür. local = hizalama matematiği için (tracking uzayı),
    /// worldPoint = silindiri koymak için (dünya uzayı). Dik tutman şart değil.
    /// </summary>
    private Vector3 GetFloorPoint(out Vector3 worldPoint)
    {
        Vector3 localPos = rigRoot.InverseTransformPoint(rightController.position);
        Vector3 localFwd = rigRoot.InverseTransformDirection(rightController.forward).normalized;

        if (Mathf.Abs(localFwd.y) < 0.05f)
        {
            Debug.LogWarning("[Calib] Controller yere doğru bakmıyor; noktayı alamadım.");
            worldPoint = rightController.position;
            return localPos;
        }

        float t = -localPos.y / localFwd.y;
        if (t < 0f)
        {
            Debug.LogWarning("[Calib] Zemin controller'ın arkasında; aşağı doğru tut.");
            worldPoint = rightController.position;
            return localPos;
        }

        Vector3 local = localPos + localFwd * t; // tracking uzayında zemin noktası
        worldPoint = rigRoot.TransformPoint(local);
        return local;
    }

    private void Apply()
    {
        Vector3 pA = Flat(_trackedA), pB = Flat(_trackedB);
        Vector3 gA = Flat(gamePointA.position), gB = Flat(gamePointB.position);

        Vector3 pDir = (pB - pA).normalized; // fiziksel yön
        Vector3 gDir = (gB - gA).normalized; // oyun yönü

        if (pDir.sqrMagnitude < 0.0001f || gDir.sqrMagnitude < 0.0001f)
        {
            Debug.LogWarning("[Calib] A ve B çok yakın, yön hesaplanamadı. Noktaları ayır.");
            ClearMarkers();
            _hasA = _hasB = false;
            return;
        }

        float pYaw = Mathf.Atan2(pDir.x, pDir.z) * Mathf.Rad2Deg;
        float gYaw = Mathf.Atan2(gDir.x, gDir.z) * Mathf.Rad2Deg;
        float deltaYaw = gYaw - pYaw;

        Quaternion rot = Quaternion.Euler(0f, deltaYaw, 0f);

        // trackingA -> gameA, trackingB -> gameB
        rigRoot.rotation = rot;
        rigRoot.position = gA - rot * pA;

        Debug.Log($"[Calib] Hizalandı. deltaYaw = {deltaYaw:F1}°");

        // Hizalama bitti: iki silindiri de geri sil
        ClearMarkers();
    }

    // --- İşaretçi yönetimi ---

    private void SpawnMarker(ref GameObject marker, Vector3 worldPos)
    {
        if (marker != null) Destroy(marker); // aynı noktaya tekrar basılırsa eskisini at

        marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "CalibMarker";

        // Çarpışmaya gerek yok
        var col = marker.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Unity silindir primitive'i 2 birim boyunda; yarısı = istenen yükseklik
        marker.transform.localScale = new Vector3(markerDiameter, markerHeight * 0.5f, markerDiameter);
        // Tabanı zemine otursun diye merkezi yarı yükseklik yukarı taşı
        marker.transform.position = worldPos + Vector3.up * (markerHeight * 0.5f);

        var rend = marker.GetComponent<Renderer>();
        if (rend != null) rend.material.color = markerColor;
    }

    private void ClearMarkers()
    {
        if (_markerA != null) { Destroy(_markerA); _markerA = null; }
        if (_markerB != null) { Destroy(_markerB); _markerB = null; }
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private void OnDrawGizmos()
    {
        if (gamePointA) { Gizmos.color = Color.green; Gizmos.DrawSphere(gamePointA.position, 0.1f); }
        if (gamePointB) { Gizmos.color = Color.red; Gizmos.DrawSphere(gamePointB.position, 0.1f); }
    }
}