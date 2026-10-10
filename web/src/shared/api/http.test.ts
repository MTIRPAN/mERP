import { afterEach, describe, expect, it, vi } from 'vitest';
import { configureHttp, customFetch } from './http';
import { ApiError } from './problem';

const jsonResponse = (status: number, body: unknown, contentType = 'application/json') =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': contentType } });

describe('customFetch', () => {
  afterEach(() => configureHttp({ getAccessToken: () => Promise.resolve(undefined), getCompanyId: () => undefined }));

  it('sends the token, culture and active company', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(200, { ok: true }));
    configureHttp({
      getAccessToken: () => Promise.resolve('token-1'),
      getCompanyId: () => 'company-1',
      getCulture: () => 'tr-TR',
    });

    const result = await customFetch<{ ok: boolean }>('/api/platform/me', { method: 'GET' });

    expect(result).toEqual({ ok: true });
    const headers = new Headers(fetchMock.mock.calls[0]?.[1]?.headers);
    expect(headers.get('Authorization')).toBe('Bearer token-1');
    expect(headers.get('Accept-Language')).toBe('tr-TR');
    expect(headers.get('X-Company-Id')).toBe('company-1');
  });

  it('turns ProblemDetails into ApiError', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        400,
        { title: 'İstek geçersiz', detail: 'Bir veya daha fazla alan geçersiz.', code: 'common.errors.validation' },
        'application/problem+json',
      ),
    );

    const error = await customFetch('/api/x').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).problem?.code).toBe('common.errors.validation');
    expect((error as ApiError).message).toBe('Bir veya daha fazla alan geçersiz.');
  });

  it('keeps attachments binary, even text files', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response('Ad;Şehir', {
        status: 200,
        headers: { 'content-type': 'text/csv; charset=utf-8', 'content-disposition': 'attachment; filename=liste.csv' },
      }),
    );

    const result = await customFetch<Blob>('/api/platform/files/1');

    // Not instanceof: under jsdom, fetch's Blob may come from Node's realm, not the window's
    expect(typeof result).toBe('object');
    expect(await result.text()).toBe('Ad;Şehir');
  });

  it('reports 401 and 403 to the application', async () => {
    const onUnauthorized = vi.fn();
    const onForbidden = vi.fn();
    configureHttp({ onUnauthorized, onForbidden });
    const fetchMock = vi.spyOn(globalThis, 'fetch');

    fetchMock.mockResolvedValueOnce(jsonResponse(401, {}, 'application/problem+json'));
    await expect(customFetch('/api/x')).rejects.toBeInstanceOf(ApiError);
    fetchMock.mockResolvedValueOnce(jsonResponse(403, {}, 'application/problem+json'));
    await expect(customFetch('/api/x')).rejects.toBeInstanceOf(ApiError);

    expect(onUnauthorized).toHaveBeenCalledOnce();
    expect(onForbidden).toHaveBeenCalledOnce();
  });

  it('reports an unreachable server as status 0', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(customFetch('/api/x')).rejects.toMatchObject({ status: 0 });
  });

  it('keeps files binary', async () => {
    const xlsx = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(new Uint8Array([80, 75, 3, 4]), { status: 200, headers: { 'content-type': xlsx } }),
    );

    const file = await customFetch<Blob>('/api/x/export');

    // Not instanceof: under jsdom, fetch's Blob may come from Node's realm, not the window's
    expect(file.type).toBe(xlsx);
    expect(new Uint8Array(await file.arrayBuffer())).toEqual(new Uint8Array([80, 75, 3, 4]));
  });
});
