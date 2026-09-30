using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class ScrapPickup : MonoBehaviour
{
    [SerializeField] private int scrapValue = 1;

    private Collider pickupCollider;
    private Rigidbody pickupRigidbody;
    private bool collected;

    private void Awake()
    {
        pickupCollider = GetComponent<Collider>();
        pickupRigidbody = GetComponent<Rigidbody>();

        pickupCollider.isTrigger = true;
        pickupRigidbody.useGravity = false;
        pickupRigidbody.isKinematic = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected)
        {
            return;
        }

        ScrapCollector collector = other.GetComponentInParent<ScrapCollector>();

        if (collector == null)
        {
            return;
        }

        collected = true;
        collector.AddScrap(scrapValue);
        Destroy(gameObject);
    }
}