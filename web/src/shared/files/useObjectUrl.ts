import { useEffect, useState } from 'react';

/** Object URL of a downloaded picture, released when it changes or the component goes away. */
export const useObjectUrl = (blob: unknown) => {
  const [url, setUrl] = useState<string>();
  useEffect(() => {
    if (!(blob instanceof Blob)) {
      setUrl(undefined);
      return undefined;
    }

    const created = URL.createObjectURL(blob);
    setUrl(created);
    return () => URL.revokeObjectURL(created);
  }, [blob]);
  return url;
};
