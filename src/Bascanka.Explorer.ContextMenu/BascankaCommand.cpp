#include "BascankaCommand.h"
#include <shellapi.h>
#include <shlobj_core.h>
#include <strsafe.h>

// The CLSID is read from registry during registration.
// This must match the CLSID the C# app registers.
// We store it here as an extern that dllmain.cpp defines after reading from registry.
extern CLSID g_CLSID_BascankaCommand;

BascankaCommand::BascankaCommand() : m_refCount(1)
{
}

// ---- IUnknown ----

HRESULT BascankaCommand::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;

    *ppv = nullptr;

    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IExplorerCommand))
    {
        *ppv = static_cast<IExplorerCommand*>(this);
        AddRef();
        return S_OK;
    }

    return E_NOINTERFACE;
}

ULONG BascankaCommand::AddRef()
{
    return InterlockedIncrement(&m_refCount);
}

ULONG BascankaCommand::Release()
{
    LONG ref = InterlockedDecrement(&m_refCount);
    if (ref == 0)
    {
        delete this;
    }
    return ref;
}

// ---- IExplorerCommand ----

HRESULT BascankaCommand::GetTitle(IShellItemArray* /*psiItemArray*/, LPWSTR* ppszName)
{
    std::wstring displayName = ReadRegString(L"DisplayName");
    if (displayName.empty())
    {
        displayName = L"Edit with Bascanka";
    }
    return SHStrDupW(displayName.c_str(), ppszName);
}

HRESULT BascankaCommand::GetIcon(IShellItemArray* /*psiItemArray*/, LPWSTR* ppszIcon)
{
    std::wstring iconPath = ReadRegString(L"IconPath");
    if (iconPath.empty())
    {
        // Fallback: use the exe itself as icon source
        iconPath = ReadRegString(L"ExePath");
        if (!iconPath.empty())
        {
            iconPath += L",0";
        }
    }

    if (iconPath.empty())
    {
        *ppszIcon = nullptr;
        return E_NOTIMPL;
    }

    return SHStrDupW(iconPath.c_str(), ppszIcon);
}

HRESULT BascankaCommand::GetToolTip(IShellItemArray* /*psiItemArray*/, LPWSTR* ppszInfotip)
{
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

HRESULT BascankaCommand::GetCanonicalName(GUID* pguidCommandName)
{
    *pguidCommandName = g_CLSID_BascankaCommand;
    return S_OK;
}

HRESULT BascankaCommand::GetState(IShellItemArray* /*psiItemArray*/, BOOL /*fOkToBeSlow*/, EXPCMDSTATE* pCmdState)
{
    // Always enabled. You could add logic here to check file types, etc.
    *pCmdState = ECS_ENABLED;
    return S_OK;
}

HRESULT BascankaCommand::GetFlags(EXPCMDFLAGS* pFlags)
{
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

HRESULT BascankaCommand::Invoke(IShellItemArray* psiItemArray, IBindCtx* /*pbc*/)
{
    if (!psiItemArray)
        return E_INVALIDARG;

    std::wstring exePath = ReadRegString(L"ExePath");
    if (exePath.empty())
        return E_FAIL;

    DWORD count = 0;
    HRESULT hr = psiItemArray->GetCount(&count);
    if (FAILED(hr) || count == 0)
        return hr;

    // Build a combined argument string for all selected files
    std::wstring args;

    for (DWORD i = 0; i < count; i++)
    {
        IShellItem* psi = nullptr;
        hr = psiItemArray->GetItemAt(i, &psi);
        if (SUCCEEDED(hr) && psi)
        {
            LPWSTR filePath = nullptr;
            hr = psi->GetDisplayName(SIGDN_FILESYSPATH, &filePath);
            if (SUCCEEDED(hr) && filePath)
            {
                if (!args.empty())
                    args += L' ';

                args += L'"';
                args += filePath;
                args += L'"';

                CoTaskMemFree(filePath);
            }
            psi->Release();
        }
    }

    if (args.empty())
        return E_FAIL;

    // Launch the application
    SHELLEXECUTEINFOW sei = { sizeof(sei) };
    sei.fMask = SEE_MASK_DEFAULT;
    sei.lpVerb = L"open";
    sei.lpFile = exePath.c_str();
    sei.lpParameters = args.c_str();
    sei.nShow = SW_SHOWNORMAL;

    if (!ShellExecuteExW(&sei))
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    return S_OK;
}

HRESULT BascankaCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum)
{
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

// ---- Helper ----

std::wstring BascankaCommand::ReadRegString(const wchar_t* valueName) const
{
    HKEY hKey = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, CONFIG_REG_KEY, 0, KEY_READ, &hKey) != ERROR_SUCCESS)
    {
        return {};
    }

    wchar_t buffer[1024] = {};
    DWORD bufferSize = sizeof(buffer);
    DWORD type = 0;

    LSTATUS status = RegQueryValueExW(hKey, valueName, nullptr, &type,
        reinterpret_cast<LPBYTE>(buffer), &bufferSize);

    RegCloseKey(hKey);

    if (status != ERROR_SUCCESS || type != REG_SZ)
    {
        return {};
    }

    return std::wstring(buffer);
}
