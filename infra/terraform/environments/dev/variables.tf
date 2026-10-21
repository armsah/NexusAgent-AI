variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "location" {
  description = "Azure deployment region"
  type        = string
  default     = "swedencentral"
}

variable "prefix" {
  description = "Resource naming prefix"
  type        = string
  default     = "nexusagent"
}

variable "environment" {
  description = "Deployment environment"
  type        = string
  default     = "dev"
}

variable "postgres_admin_login" {
  description = "PostgreSQL administrator username"
  type        = string
  default     = "nexusadmin"
}

variable "postgres_admin_password" {
  description = "PostgreSQL administrator password"
  type        = string
  sensitive   = true
  default     = null
}

variable "postgres_allowed_ip" {
  description = "Allowed development client IPv4 address"
  type        = string
  default     = ""
}

variable "container_image" {
  description = "Full container image reference"
  type        = string
}

variable "deploy_api" {
  description = "Whether to deploy the API container"
  type        = bool
  default     = false
}

variable "enable_ai_services" {
  description = "Whether to provision optional AI services"
  type        = bool
  default     = false
}

variable "tags" {
  description = "Additional Azure resource tags"
  type        = map(string)
  default     = {}
}

variable "deploy_data_services" {
  description = "Whether to provision PostgreSQL resources"
  type        = bool
  default     = false
}
