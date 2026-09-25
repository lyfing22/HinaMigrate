<script setup lang="ts">
import { Card, Descriptions, DescriptionsItem, Typography, Tag } from 'ant-design-vue'
import { metaStore } from '@/stores/meta'
</script>

<template>
  <Card title="关于" size="small">
    <Descriptions :column="1" bordered size="small">
      <DescriptionsItem label="应用名称">福建影像数据迁移(桌面端)</DescriptionsItem>
      <DescriptionsItem label="版本">0.1.0</DescriptionsItem>
      <DescriptionsItem label="技术架构">Tauri v2 + C# Sidecar(IPC) + Vue 3 + Ant Design Vue</DescriptionsItem>
      <DescriptionsItem label="应用数据目录">{{ metaStore.appDataDir.value || '(加载中)' }}</DescriptionsItem>
      <DescriptionsItem label="配置档目录">{{ metaStore.appDataDir.value ? metaStore.appDataDir.value + '\\profiles' : '-' }}</DescriptionsItem>
      <DescriptionsItem label="日志目录">{{ metaStore.appDataDir.value ? metaStore.appDataDir.value + '\\logs' : '-' }}</DescriptionsItem>
      <DescriptionsItem label="支持的源库类型">
        <Tag v-for="t in metaStore.supported.value.source" :key="t" color="blue">{{ t }}</Tag>
      </DescriptionsItem>
      <DescriptionsItem label="支持的目标库类型">
        <Tag v-for="t in metaStore.supported.value.dest" :key="t" color="green">{{ t }}</Tag>
      </DescriptionsItem>
    </Descriptions>
    <Typography type="secondary" style="display: block; margin-top: 12px">
      提示：源/目标连接、上传参数在「配置管理」页录入；首次使用请先「测试连接」并「初始化目标库」。
    </Typography>
  </Card>
</template>
