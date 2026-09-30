using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace CrazyGames
{
    public class StoreModuleDemo : MonoBehaviour
    {
        private const string XsollaProjectId = "213755";

        public InputField skuInput;
        public Toggle sandboxToggle;
        public Button buyButton;
        public Button listInventoryButton;

        private void Start()
        {
            sandboxToggle.onValueChanged.AddListener(ToggleSandbox);
            buyButton.onClick.AddListener(BuyItem);
            listInventoryButton.onClick.AddListener(ListInventory);

            CrazySDK.Init(() =>
            {
                CrazySDK.Store.SetSandbox(sandboxToggle.isOn);
            });
        }

        private void ToggleSandbox(bool sandbox)
        {
            if (!CrazySDK.IsInitialized)
            {
                return;
            }

            Debug.Log("Use sandbox payments: " + sandbox);
            CrazySDK.Store.SetSandbox(sandbox);
        }

        private async void BuyItem()
        {
            var sku = skuInput.text.Trim();
            if (string.IsNullOrEmpty(sku))
            {
                Debug.Log("No SKU provided");
                return;
            }

            if (MainDemoScene.UseAsyncMethods)
            {
                try
                {
                    var result = await CrazySDK.Store.BuyItemAsync(sku);
                    Debug.Log("Purchase successful (async): " + result);
                }
                catch (SdkError e)
                {
                    Debug.LogError("Purchase failed (async): " + e);
                }
            }
            else
            {
                CrazySDK.Store.BuyItem(
                    sku,
                    (error, result) =>
                    {
                        if (error != null)
                        {
                            Debug.LogError("Purchase failed: " + error);
                            return;
                        }

                        Debug.Log("Purchase successful: " + result);
                    }
                );
            }
        }

        private void ListInventory()
        {
            CrazySDK.User.GetXsollaUserToken(
                (error, token) =>
                {
                    if (error != null)
                    {
                        Debug.LogError("Get Xsolla user token error: " + error);
                        return;
                    }

                    StartCoroutine(FetchInventory(token));
                }
            );
        }

        // this method is here for demo purposes
        private IEnumerator FetchInventory(string xsollaToken)
        {
            var url = $"https://store.xsolla.com/api/v2/project/{XsollaProjectId}/user/inventory/items";
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", "Bearer " + xsollaToken);
                yield return request.SendWebRequest();

                if (!string.IsNullOrEmpty(request.error))
                {
                    // in the editor the SDK returns a demo Xsolla token, so this request fails with 401
                    Debug.LogError($"Get inventory error: {request.responseCode} {request.error}, {request.downloadHandler.text}");
                    yield break;
                }

                Debug.Log("Get inventory response: " + request.downloadHandler.text);
                var inventory = JsonUtility.FromJson<XsollaInventory>(request.downloadHandler.text);
                if (inventory == null || inventory.items == null || inventory.items.Count == 0)
                {
                    Debug.Log("Inventory is empty");
                    yield break;
                }

                foreach (var item in inventory.items)
                {
                    Debug.Log($"Inventory item: {item.name} x{item.quantity} ({item.sku})");
                }
            }
        }

        [Serializable]
        private class XsollaInventory
        {
            public List<XsollaInventoryItem> items;
        }

        [Serializable]
        private class XsollaInventoryItem
        {
            public string sku;
            public string name;
            public int quantity;
        }
    }
}
