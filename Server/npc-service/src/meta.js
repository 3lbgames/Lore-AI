import { ServiceError } from './service.js';

export function metaIdentityVerifier({ appId, appSecret, fetchImpl = fetch }) {
  return async (identity, signal) => {
    if (!identity || !/^\d{1,30}$/.test(identity.userId) || typeof identity.nonce !== 'string' || !identity.nonce || identity.nonce.length > 4096)
      throw new ServiceError(401, 'invalid_identity');
    const response = await fetchImpl('https://graph.oculus.com/user_nonce_validate', {
      method: 'POST', signal,
      body: new URLSearchParams({ access_token: `OC|${appId}|${appSecret}`, user_id: identity.userId, nonce: identity.nonce })
    });
    if (!response.ok) throw new ServiceError(401, 'identity_rejected');
    const result = await response.json();
    if (result.is_valid !== true) throw new ServiceError(401, 'identity_rejected');
    return identity.userId;
  };
}
