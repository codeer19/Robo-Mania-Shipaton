using UnityEngine;

public class ScrapCollector : MonoBehaviour
{
    public int Scrap { get; private set; }

    public void AddScrap(int amount)
    {
        Scrap += amount;
        Debug.Log("Scrap collected. Total Scrap: " + Scrap);
    }
}