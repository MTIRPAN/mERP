import { UserManager, WebStorageStateStore } from 'oidc-client-ts';

/** Keycloak (D5): authorization code flow with PKCE, tokens renewed silently with the refresh token. */
export const userManager = new UserManager({
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://localhost:8080/realms/merp',
  client_id: import.meta.env.VITE_OIDC_CLIENT_ID ?? 'merp-web',
  redirect_uri: `${window.location.origin}/auth/callback`,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  scope: 'openid profile email',
  automaticSilentRenew: true,
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
});

export const getAccessToken = async (): Promise<string | undefined> => {
  const user = await userManager.getUser();
  return user && !user.expired ? user.access_token : undefined;
};

export interface SignInState {
  returnTo?: string;
}

export const signIn = (returnTo: string = window.location.pathname + window.location.search) =>
  userManager.signinRedirect({ state: { returnTo } satisfies SignInState });
