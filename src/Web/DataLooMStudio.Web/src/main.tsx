import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { MsalProvider } from '@azure/msal-react'
import { PublicClientApplication } from '@azure/msal-browser'
import App from './App'
import { runtimeConfig } from './runtimeConfig'
import './style.css'

const msal = new PublicClientApplication({
  auth: {
    clientId: runtimeConfig.spaClientId,
    authority: runtimeConfig.entraAuthority,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: { cacheLocation: 'sessionStorage' },
})

await msal.initialize()
const response = await msal.handleRedirectPromise()
if (response?.account) {
  msal.setActiveAccount(response.account)
} else if (!msal.getActiveAccount() && msal.getAllAccounts().length > 0) {
  msal.setActiveAccount(msal.getAllAccounts()[0])
}

createRoot(document.getElementById('root')!).render(
  <StrictMode><MsalProvider instance={msal}><App /></MsalProvider></StrictMode>,
)
