# PortQueue 前端产品范围

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

单港口业务人员查询预约与审核结果；课程中期答辩由开发者演示数据库成果。

## Product Purpose

围绕进港预约、锚地排队、泊位时间窗与实际事件形成可追溯的管理系统。阶段 2 只完成真实预约查询。

## Capabilities and Constraints

已由本会话确认：Vue 3 + TypeScript + Vite，后端 .NET 8，SQL Server。中期前完成只读查询、筛选、分页、加载/空/错误状态；时间显示 Asia/Shanghai。现有总览是合成数据原型，不能冒充实际功能。

## Brand Commitments

名称 PortQueue，中文业务术语。用户要求避免无意义组件、营销式大标题、虚假指标和 AI 模板感；采用标题、筛选、表格与分页的工作界面。

## Evidence on Hand

database/10_seed_demo.sql 是持久化的合成业务样例。阶段 1 数据库规则验收已完成；真实港口/AIS 数据、正式登录系统尚未接入。

## Product Principles

查询结果来自数据库；失败不能回退 Mock。申请时间与实际时间分清。阶段范围小、可验证，默认首页打开已完成的真实功能。
