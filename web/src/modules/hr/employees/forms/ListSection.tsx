import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Col, Divider, Flex, Form, Row } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

interface ListSectionProps {
  /** Form list name, e.g. "emails". */
  name: string;
  title: ReactNode;
  addLabel: ReactNode;
  /** The row's fields; <c>field</c> is the row's index for nested names. */
  children: (field: number) => ReactNode;
  /** Values of a new row. */
  initialValue?: Record<string, unknown>;
}

/** A titled, editable list of rows (Form.List) with add and remove; each row is a grid of fields. */
export const ListSection = ({ name, title, addLabel, children, initialValue }: ListSectionProps) => {
  const { t } = useTranslation();
  return (
    <>
      <Divider titlePlacement="start">{title}</Divider>
      <Form.List name={name}>
        {(fields, { add, remove }) => (
          <Flex vertical gap={4}>
            {fields.map((field) => (
              <Row key={field.key} gutter={8} align="top" wrap={false}>
                <Col flex="auto">
                  <Row gutter={8}>{children(field.name)}</Row>
                </Col>
                <Col flex="32px" style={{ paddingTop: 30 }}>
                  <Button
                    type="text"
                    danger
                    icon={<DeleteOutlined />}
                    aria-label={t('web.common.delete')}
                    onClick={() => remove(field.name)}
                  />
                </Col>
              </Row>
            ))}
            <Button type="dashed" icon={<PlusOutlined />} onClick={() => add(initialValue)} block>
              {addLabel}
            </Button>
          </Flex>
        )}
      </Form.List>
    </>
  );
};
