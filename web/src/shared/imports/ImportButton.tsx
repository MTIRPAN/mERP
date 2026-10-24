import { FileExcelOutlined } from '@ant-design/icons';
import { Button } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ImportDrawer } from './ImportDrawer';

interface ImportButtonProps {
  /** The import, e.g. hr.employees. */
  importKey: string;
  /** Name of the records, e.g. "Personel": file names and the title. */
  name: string;
  /** Records were written: refresh the page's lists. */
  onImported: () => void;
  /** Rows of existing records may update them (D40). */
  supportsUpdate?: boolean;
}

/** "Excel'den içe aktar" with its drawer (D35), for the toolbar of a list page. */
export const ImportButton = ({ importKey, name, onImported, supportsUpdate }: ImportButtonProps) => {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button icon={<FileExcelOutlined />} onClick={() => setOpen(true)}>
        {t('web.imports.button')}
      </Button>
      {/* Mounted only while open, so the list page does not load the import hooks */}
      {open && (
        <ImportDrawer
          importKey={importKey}
          name={name}
          open
          onClose={() => setOpen(false)}
          onImported={onImported}
          supportsUpdate={supportsUpdate}
        />
      )}
    </>
  );
};
