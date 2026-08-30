export type DataSourcePayload = {
  event: string;
  items: DataSourceItem[];
};

export type DataSourceItem = {
  disabled?: boolean;
  label: string;
  value: string;
};
