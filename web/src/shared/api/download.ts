/**
 * Saves a file returned by a generated API function (Excel exports). The client returns binary responses as a Blob
 * (see customFetch), while the generated signature says void because the OpenAPI document has no schema for files.
 */
export const saveFile = async (request: Promise<unknown>, fileName: string): Promise<void> => {
  const file = await request;
  if (!(file instanceof Blob)) {
    throw new Error('The response is not a file');
  }

  const url = URL.createObjectURL(file);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  // Some browsers read the URL after click() returns
  setTimeout(() => URL.revokeObjectURL(url), 1000);
};
