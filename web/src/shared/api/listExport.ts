import { App } from 'antd';
import dayjs from 'dayjs';
import { useCallback } from 'react';
import { saveFile } from './download';
import { isApiError } from './problem';

/**
 * Downloads a list export (D35) as "{name}-{date}.xlsx". A refused export (too many rows, an invalid sort) shows the
 * server's message; field errors are not shown by ApiErrorNotifier.
 */
export const useListExport = () => {
  const { message } = App.useApp();
  return useCallback(
    async (request: Promise<unknown>, name: string) => {
      try {
        await saveFile(request, `${name}-${dayjs().format('YYYY-MM-DD')}.xlsx`);
      } catch (error) {
        const detail = isApiError(error) ? error.problem?.fieldErrors?.[0]?.message : undefined;
        if (detail) {
          void message.error(detail);
        }
      }
    },
    [message],
  );
};
