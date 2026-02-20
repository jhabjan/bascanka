#include <windows.h>
#include <new>
#include <string>
#include <guiddef.h>
#include "ClassFactory.h"

// Global ref count for DllCanUnloadNow
LONG g_dllRefCount = 0;

// The CLSID - read from registry at load time
CLSID g_CLSID_BascankaCommand = { 0 };

HINSTANCE g_hModule = nullptr;

static bool ReadCLSIDFromRegistry()
{
    HKEY hKey = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Bascanka\\ContextMenu", 0, KEY_READ, &hKey) != ERROR_SUCCESS)
    {
        return false;
    }

    wchar_t buffer[128] = {};
    DWORD bufferSize = sizeof(buffer);
    DWORD type = 0;

    LSTATUS status = RegQueryValueExW(hKey, L"CLSID", nullptr, &type,
        reinterpret_cast<LPBYTE>(buffer), &bufferSize);
    RegCloseKey(hKey);

    if (status != ERROR_SUCCESS || type != REG_SZ)
    {
        return false;
    }

    // Parse the CLSID string, e.g. "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}"
    return SUCCEEDED(CLSIDFromString(buffer, &g_CLSID_BascankaCommand));
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID /*lpReserved*/)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
        ReadCLSIDFromRegistry();
        break;
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

// ---- Exported Functions ----

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (!ppv)
        return E_POINTER;

    *ppv = nullptr;

    // If CLSID wasn't loaded, try again
    if (g_CLSID_BascankaCommand == CLSID{})
    {
        ReadCLSIDFromRegistry();
    }

    if (!IsEqualCLSID(rclsid, g_CLSID_BascankaCommand))
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    ClassFactory* pFactory = new (std::nothrow) ClassFactory();
    if (!pFactory)
        return E_OUTOFMEMORY;

    HRESULT hr = pFactory->QueryInterface(riid, ppv);
    pFactory->Release();
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return (g_dllRefCount > 0) ? S_FALSE : S_OK;
}
