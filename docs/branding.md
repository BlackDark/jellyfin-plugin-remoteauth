# Jellyfin branding button

Adds a "Sign in with SSO" button on the Jellyfin login page that opens `/sso/RemoteAuth/Login`.

## Setup

1. In Jellyfin: **Admin Dashboard → Branding**
2. Paste the HTML into the custom login / disclaimer (upper) box
3. Paste the CSS into the custom CSS box
4. Save

The button only works when the request goes through your reverse proxy with secret + identity headers on `GET /sso/RemoteAuth/Login`. Direct hits to Jellyfin without those headers fail auth.

If Jellyfin uses a **Base URL** / subpath (for example `/jellyfin`), set the form action to that prefix + `/sso/RemoteAuth/Login` (for example `/jellyfin/sso/RemoteAuth/Login`).

## HTML

```html
<div class="remote-auth-login">
  <p class="remote-auth-divider">or</p>
  <form action="/sso/RemoteAuth/Login" method="get">
    <button type="submit" class="raised block emby-button button-submit remote-auth-button">
      Sign in with SSO
    </button>
  </form>
</div>
```

## CSS

```css
.loginDisclaimerContainer,
.remote-auth-login {
  display: block;
  width: 100%;
  max-width: 22em;
  margin: 0.75em auto 0;
}

.remote-auth-divider {
  text-align: center;
  opacity: 0.6;
  margin: 0.5em 0 1em;
  font-size: 0.9em;
}

.remote-auth-button {
  width: 100%;
  padding: 0.95em 1em;
  border: 0;
  border-radius: 0.35em;
  font-weight: 600;
  letter-spacing: 0.02em;
  background: linear-gradient(135deg, #00a4dc 0%, #0088b8 100%);
  color: #fff !important;
  box-shadow: 0 4px 14px rgba(0, 164, 220, 0.35);
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.remote-auth-button:hover {
  transform: translateY(-1px);
  box-shadow: 0 6px 18px rgba(0, 164, 220, 0.45);
}

.remote-auth-button:active {
  transform: translateY(0);
}
```
