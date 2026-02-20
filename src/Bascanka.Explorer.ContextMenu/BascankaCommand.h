#pragma once

#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <string>

// Registry key where the C# registrar writes configuration
// HKCU\Software\Bascanka\ContextMenu
//   DisplayName  (REG_SZ)  - e.g. "Edit with Bascanka"
//   ExePath      (REG_SZ)  - e.g. "C:\Program Files\Bascanka\Bascanka.exe"
//   IconPath     (REG_SZ)  - e.g. "C:\Program Files\Bascanka\Bascanka.exe,0"

constexpr const wchar_t* CONFIG_REG_KEY = L"Software\\Bascanka\\ContextMenu";

class BascankaCommand : public IExplorerCommand
{
public:
    BascankaCommand();

    // IUnknown
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override;
    ULONG   STDMETHODCALLTYPE AddRef() override;
    ULONG   STDMETHODCALLTYPE Release() override;

    // IExplorerCommand
    HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray* psiItemArray, LPWSTR* ppszName) override;
    HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray* psiItemArray, LPWSTR* ppszIcon) override;
    HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray* psiItemArray, LPWSTR* ppszInfotip) override;
    HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* pguidCommandName) override;
    HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* psiItemArray, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) override;
    HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* pFlags) override;
    HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* psiItemArray, IBindCtx* pbc) override;
    HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** ppEnum) override;

private:
    LONG m_refCount;

    std::wstring ReadRegString(const wchar_t* valueName) const;
};
