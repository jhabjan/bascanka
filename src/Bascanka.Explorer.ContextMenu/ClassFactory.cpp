#include "ClassFactory.h"
#include "BascankaCommand.h"

extern LONG g_dllRefCount;

ClassFactory::ClassFactory() : m_refCount(1)
{
    InterlockedIncrement(&g_dllRefCount);
}

// ---- IUnknown ----

HRESULT ClassFactory::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;

    *ppv = nullptr;

    if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
    {
        *ppv = static_cast<IClassFactory*>(this);
        AddRef();
        return S_OK;
    }

    return E_NOINTERFACE;
}

ULONG ClassFactory::AddRef()
{
    return InterlockedIncrement(&m_refCount);
}

ULONG ClassFactory::Release()
{
    LONG ref = InterlockedDecrement(&m_refCount);
    if (ref == 0)
    {
        InterlockedDecrement(&g_dllRefCount);
        delete this;
    }
    return ref;
}

// ---- IClassFactory ----

HRESULT ClassFactory::CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv)
{
    if (pUnkOuter)
        return CLASS_E_NOAGGREGATION;

    BascankaCommand* pCmd = new (std::nothrow) BascankaCommand();
    if (!pCmd)
        return E_OUTOFMEMORY;

    HRESULT hr = pCmd->QueryInterface(riid, ppv);
    pCmd->Release();
    return hr;
}

HRESULT ClassFactory::LockServer(BOOL fLock)
{
    if (fLock)
        InterlockedIncrement(&g_dllRefCount);
    else
        InterlockedDecrement(&g_dllRefCount);
    return S_OK;
}
